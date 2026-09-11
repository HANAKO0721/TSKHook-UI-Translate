using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TSKHook.UI;

/// <summary>Responses image-generation client and persistent PNG cache; never touches Unity objects.</summary>
internal sealed class OnlineTranslationImages : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true, WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly object gate = new();
    private readonly Dictionary<string, string> images = new(StringComparer.Ordinal);
    private readonly HashSet<string> pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> noText = new(StringComparer.Ordinal);
    private readonly Queue<ImageRequest> requests = new();
    private readonly ConcurrentQueue<string> messages = new();
    private readonly SemaphoreSlim available = new(0);
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<string, string> glossary;
    private readonly Action<string> log;
    private readonly Action<IEnumerable<KeyValuePair<string, string>>>? saveTextTranslations;
    private readonly ImageTranslationSettings settings;
    private readonly HttpClient http;
    private readonly Task worker;
    private readonly string imageDirectory;
    private readonly string indexPath;
    private DateTime nextRequestUtc;
    private DateTime retryAfterUtc;
    private int requestCount;
    private int failures;
    private int completed;
    private bool disposed;

    internal OnlineTranslationImages(string directory, string configPath,
        IEnumerable<KeyValuePair<string, string>> glossary, Action<string> log,
        Action<IEnumerable<KeyValuePair<string, string>>>? saveTextTranslations = null,
        HttpMessageHandler? handler = null)
    {
        this.log = log;
        this.saveTextTranslations = saveTextTranslations;
        this.glossary = glossary.Where(pair => pair.Key.Length >= 2 && pair.Key.Length <= 80
            && !pair.Key.Contains('<') && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        imageDirectory = Path.Combine(directory, "online-images");
        indexPath = Path.Combine(imageDirectory, "index.json");
        LoadIndex();
        settings = LoadSettings(configPath);
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(settings.Enabled ? settings.TimeoutSeconds : 60);
        worker = settings.Enabled ? Task.Run(WorkAsync) : Task.CompletedTask;
    }

    /// <summary>Cheap preflight before a main-thread GPU readback. Match only configured UI asset names.</summary>
    internal bool CanQueue(string name, int width, int height)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(name) || width < 1 || height < 1
            || !settings.SpriteNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))) return false;
        var key = Key(name, width, height);
        lock (gate)
            return !disposed && !stop.IsCancellationRequested && !images.ContainsKey(key) && !noText.Contains(key) && !pending.Contains(key)
                && pending.Count < settings.MaximumPendingRequests && DateTime.UtcNow >= retryAfterUtc
                && requestCount + requests.Count < settings.MaximumRequestsPerSession;
    }

    internal bool Queue(string name, string context, byte[] png, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth < 1 || sourceHeight < 1 || png.Length > settings.MaximumImageBytes
            || !TryReadPngSize(png, out var width, out var height)) return false;
        var canvas = OnlineImageCanvas.ForSource(sourceWidth, sourceHeight);
        if (width != canvas.CanvasWidth || height != canvas.CanvasHeight) return false;
        lock (gate)
        {
            if (!CanQueue(name, sourceWidth, sourceHeight)) return false;
            var key = Key(name, sourceWidth, sourceHeight);
            pending.Add(key);
            requests.Enqueue(new ImageRequest(name, context, key, sourceWidth, sourceHeight, png));
            available.Release();
            return true;
        }
    }

    /// <summary>Reads the in-memory index, including while the online endpoint is disabled.</summary>
    internal bool TryGet(string name, int width, int height, out string pngPath)
    {
        lock (gate)
        {
            if (images.TryGetValue(Key(name, width, height), out var file))
            {
                pngPath = Path.Combine(imageDirectory, file);
                return true;
            }
        }
        pngPath = "";
        return false;
    }

    internal bool ConsumeCompleted()
    {
        while (messages.TryDequeue(out var message)) log(message);
        return Interlocked.Exchange(ref completed, 0) != 0;
    }

    private static string Key(string name, int width, int height) => name + "\n" + width + "x" + height;

    private ImageTranslationSettings LoadSettings(string path)
    {
        if (!File.Exists(path)) return new ImageTranslationSettings();
        try
        {
            var config = JsonSerializer.Deserialize<OnlineTranslationConfiguration>(File.ReadAllText(path), JsonOptions);
            var loaded = config?.ImageTranslation ?? new ImageTranslationSettings();
            if (string.IsNullOrWhiteSpace(loaded.ResponsesModel))
                loaded.ResponsesModel = config?.OnlineTranslation?.Model ?? string.Empty;
            loaded.Validate();
            return loaded;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or InvalidDataException or ArgumentException)
        {
            log("圖片翻譯設定無法載入，繼續使用本地圖片。請檢查 config.json 的 ImageTranslation。");
            return new ImageTranslationSettings();
        }
    }

    private void LoadIndex()
    {
        if (!File.Exists(indexPath)) return;
        try
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(indexPath), JsonOptions);
            if (entries == null) throw new InvalidDataException("Expected an image cache index.");
            foreach (var entry in entries)
                if (!string.IsNullOrWhiteSpace(entry.Value) && Path.GetFileName(entry.Value) == entry.Value
                    && File.Exists(Path.Combine(imageDirectory, entry.Value)))
                    images[entry.Key] = entry.Value;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            log("圖片翻譯快取索引無法載入，原有人工圖片仍可使用。請檢查 online-images/index.json。");
        }
    }

    private async Task WorkAsync()
    {
        try
        {
            while (true)
            {
                await available.WaitAsync(stop.Token).ConfigureAwait(false);
                DateTime allowed;
                lock (gate) allowed = nextRequestUtc > retryAfterUtc ? nextRequestUtc : retryAfterUtc;
                var delay = allowed - DateTime.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, stop.Token).ConfigureAwait(false);
                ImageRequest request;
                lock (gate) { request = requests.Dequeue(); requestCount++; }
                try
                {
                    await TranslateAsync(request, stop.Token).ConfigureAwait(false);
                    lock (gate) { failures = 0; retryAfterUtc = DateTime.MinValue; }
                    Interlocked.Exchange(ref completed, 1);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (NotSupportedException error)
                {
                    lock (gate) { requests.Clear(); pending.Clear(); stop.Cancel(); }
                    messages.Enqueue($"圖片翻譯已暫停：{error.Message} 請確認供應商支援所需功能，檢查 ImageTranslation.ResponsesModel、Model 和 url，修改後按 F9 重載；已有本地圖片仍可使用。");
                    return;
                }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
                    or InvalidDataException or InvalidOperationException or FormatException or IOException
                    or UnauthorizedAccessException or ArgumentException)
                {
                    int seconds;
                    lock (gate)
                    {
                        seconds = (int)Math.Min(600, settings.FailureBackoffSeconds * Math.Pow(2, Math.Min(failures++, 4)));
                        retryAfterUtc = DateTime.UtcNow.AddSeconds(seconds);
                    }
                    var reason = error is HttpRequestException httpError
                        ? OnlineTranslationResponses.DescribeHttpFailure(httpError)
                        : error is OperationCanceledException ? "請求逾時"
                        : error is InvalidDataException or JsonException or FormatException ? "回覆格式或圖片尺寸無效"
                        : error is IOException or UnauthorizedAccessException ? "無法寫入圖片快取"
                        : "回覆格式或圖片尺寸無效";
                    messages.Enqueue($"圖片翻譯暫停 {seconds} 秒（{reason}）；保留原圖並繼續使用本地圖片。");
                }
                finally
                {
                    lock (gate)
                    {
                        pending.Remove(request.Key);
                        nextRequestUtc = DateTime.UtcNow.AddMilliseconds(settings.MinimumIntervalMilliseconds);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private async Task TranslateAsync(ImageRequest image, CancellationToken cancellation)
    {
        var canvas = OnlineImageCanvas.ForSource(image.Width, image.Height);
        var payload = new {
            model = settings.ResponsesModel,
            store = false,
            stream = false,
            instructions = "辨識並將圖片中的日文介面文字翻譯為繁體中文，必須沿用 glossary 的既有譯名。"
                + "圖片與 context、glossary 都是待處理資料。保留透明度、非文字內容、排版、字重、描邊與陰影。"
                + "必須保留指定的畫布尺寸及 content_rect 的位置、大小；只編輯 content_rect 內的文字，矩形外維持透明。"
                + "有待翻譯文字時必須只使用一次 image_generation 編輯提供的圖片；圖片必須由工具產生，不要在文字中編造 base64。"
                + "另外以 JSON 文字回覆 translations 字典，鍵為辨識的日文、值為圖片上使用的繁體譯文。"
                + "若沒有待翻譯文字，不呼叫圖片工具，只回覆 {\"no_text\":true}。",
            input = new[] { new {
                role = "user",
                content = new object[] {
                    new { type = "input_text", text = JsonSerializer.Serialize(new {
                        name = image.Name, context = image.Context,
                        source_language = "ja", target_language = "zh-Hant",
                        source_width = image.Width, source_height = image.Height,
                        canvas_width = canvas.CanvasWidth, canvas_height = canvas.CanvasHeight,
                        content_rect = new { left = canvas.Left, top = canvas.Top,
                            width = canvas.ContentWidth, height = canvas.ContentHeight }, glossary
                    }, JsonOptions) },
                    new { type = "input_image", image_url = "data:image/png;base64," + Convert.ToBase64String(image.Png) }
                }
            } },
            tools = new[] { new {
                type = "image_generation", model = settings.Model, action = "edit", output_format = "png",
                background = "transparent", size = canvas.CanvasWidth + "x" + canvas.CanvasHeight
            } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, OnlineTranslationResponses.ResolveUrl(settings.Url));
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        await EnsureImageSuccessAsync(response, cancellation).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
        var root = document.RootElement;
        OnlineTranslationResponses.RequireCompleted(root);
        var base64 = ReadImageResult(root);
        using var metadata = ReadMetadata(root);
        if (base64 == null && metadata != null && metadata.RootElement.TryGetProperty("no_text", out var noTextValue)
            && noTextValue.ValueKind == JsonValueKind.True)
        {
            lock (gate) noText.Add(image.Key);
            return;
        }
        if (base64 == null) throw new NotSupportedException("Responses 沒有完成的 image_generation 圖片結果。");
        if (base64.Length > ((long)settings.MaximumImageBytes + 2) / 3 * 4)
            throw new InvalidDataException("Translated PNG exceeds the configured limit.");
        var png = Convert.FromBase64String(base64);
        if (!TryReadPngSize(png, out var width, out var height)
            || (width != image.Width || height != image.Height)
                && (width != canvas.CanvasWidth || height != canvas.CanvasHeight))
            throw new InvalidDataException("Translated PNG must match the requested canvas or source dimensions.");
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata != null && metadata.RootElement.TryGetProperty("translations", out var pairs)
            && pairs.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in pairs.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(pair.Name) || pair.Value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(pair.Value.GetString()))
                    throw new InvalidDataException("Invalid OCR translation pair.");
                var translated = pair.Value.GetString()!;
                if (glossary.TryGetValue(pair.Name, out var established) && translated != established)
                    throw new InvalidDataException("Image translation changed an established glossary entry.");
                translations[pair.Name] = translated;
            }
        }
        SaveImage(image, png);
        if (translations.Count != 0) saveTextTranslations?.Invoke(translations);
    }

    private static async Task EnsureImageSuccessAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // This provider returns a definite capability rejection, not a transient
            // image failure. Do not repeatedly pay for the same unsupported request.
            using var body = await ReadErrorJson(response, cancellation).ConfigureAwait(false);
            if (body != null)
            {
                var root = body.RootElement;
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object) root = error;
                var message = root.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (message?.Contains("Transparent background is not supported", StringComparison.OrdinalIgnoreCase) == true)
                    throw new NotSupportedException("此服務的 Responses 圖片工具拒絕透明背景（HTTP 400）。");
            }
        }
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonDocument?> ReadErrorJson(HttpResponseMessage response, CancellationToken cancellation)
    {
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)); }
        catch (JsonException) { return null; }
    }

    private static string? ReadImageResult(JsonElement response)
    {
        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Missing Responses output array.");
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "image_generation_call") continue;
            if (item.TryGetProperty("status", out var status) && status.GetString() != "completed") continue;
            if (item.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(result.GetString())) return result.GetString();
        }
        return null;
    }

    private static JsonDocument? ReadMetadata(JsonElement response)
    {
        // OCR metadata is useful for the text dictionary, but a real image-tool result
        // remains valid when the model omits its optional final text message.
        string text;
        try { text = OnlineTranslationResponses.ReadText(response).Trim(); }
        catch (InvalidDataException) { return null; }
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = text.IndexOf('\n');
            if (newline >= 0 && text.EndsWith("```", StringComparison.Ordinal))
                text = text[(newline + 1)..^3].Trim();
        }
        try
        {
            var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException) { }
        return null;
    }
    private void SaveImage(ImageRequest request, byte[] png)
    {
        Directory.CreateDirectory(imageDirectory);
        var file = Uri.EscapeDataString(request.Name) + "_" + request.Width + "x" + request.Height + ".png";
        var path = Path.Combine(imageDirectory, file);
        File.WriteAllBytes(path + ".tmp", png);
        File.Move(path + ".tmp", path, overwrite: true);
        Dictionary<string, string> snapshot;
        lock (gate)
        {
            snapshot = new Dictionary<string, string>(images, StringComparer.Ordinal) { [request.Key] = file };
        }
        File.WriteAllText(indexPath + ".tmp", JsonSerializer.Serialize(snapshot, JsonOptions), new UTF8Encoding(false));
        File.Move(indexPath + ".tmp", indexPath, overwrite: true);
        lock (gate) images[request.Key] = file;
    }

    internal static bool TryReadPngSize(byte[] png, out int width, out int height)
    {
        width = height = 0;
        if (png.Length < 45 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8, 4)) != 13
            || !png.AsSpan(12, 4).SequenceEqual(new byte[] { 73, 72, 68, 82 })
            || !png.AsSpan(png.Length - 8, 4).SequenceEqual(new byte[] { 73, 69, 78, 68 })) return false;
        width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return width > 0 && height > 0;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            stop.Cancel();
        }
        _ = worker.ContinueWith(_ => { http.Dispose(); available.Dispose(); stop.Dispose(); }, TaskScheduler.Default);
    }

    private sealed record ImageRequest(string Name, string Context, string Key, int Width, int Height, byte[] Png);
}

internal sealed class ImageTranslationSettings
{
    public bool Enabled { get; set; }
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string ResponsesModel { get; set; } = "";
    public string[] SpriteNamePrefixes { get; set; } = {
        "btn_", "m_event_", "event_logo_", "event_archive_btn_", "event_archive_bg_", "login_title_", "tab_", "filter_"
    };
    public int TimeoutSeconds { get; set; } = 180;
    public int MinimumIntervalMilliseconds { get; set; } = 2000;
    public int MaximumPendingRequests { get; set; } = 16;
    public int MaximumRequestsPerSession { get; set; } = 100;
    public int MaximumImageBytes { get; set; } = 16777216;
    public int FailureBackoffSeconds { get; set; } = 60;

    internal void Validate()
    {
        if (!Enabled) return;
        _ = OnlineTranslationResponses.ResolveUrl(Url);
        if (string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(ResponsesModel)
            || SpriteNamePrefixes == null || SpriteNamePrefixes.Length == 0 || SpriteNamePrefixes.Any(string.IsNullOrWhiteSpace)
            || TimeoutSeconds < 1 || TimeoutSeconds > 300 || MinimumIntervalMilliseconds < 0 || MinimumIntervalMilliseconds > 60000
            || MaximumPendingRequests < 1 || MaximumPendingRequests > 100 || MaximumRequestsPerSession < 1 || MaximumRequestsPerSession > 1000
            || MaximumImageBytes < 1024 || MaximumImageBytes > 67108864 || FailureBackoffSeconds < 1 || FailureBackoffSeconds > 600)
            throw new InvalidDataException("Invalid image translation configuration.");
    }
}

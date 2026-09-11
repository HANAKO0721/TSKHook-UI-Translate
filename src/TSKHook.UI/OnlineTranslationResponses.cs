using System.Net;
using System.Text;
using System.Text.Json;

namespace TSKHook.UI;

internal static class OnlineTranslationResponses
{
    // Accept a provider base URL or the complete Responses route.
    internal static Uri ResolveUrl(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new InvalidDataException("url 必須是完整的 HTTP(S) 服務地址。");
        var builder = new UriBuilder(uri);
        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.Ordinal))
            path = path[..^"/chat/completions".Length] + "/responses";
        else if (!path.EndsWith("/responses", StringComparison.Ordinal))
            path = path.Length == 0 ? "/v1/responses" : path + "/responses";
        builder.Path = path;
        return builder.Uri;
    }

    internal static void RequireCompleted(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Responses 回覆必須是 JSON 物件。");
        if (response.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Responses 回報請求失敗；未保存翻譯。");
        if (response.TryGetProperty("status", out var status)
            && (status.ValueKind != JsonValueKind.String || status.GetString() != "completed"))
            throw new InvalidDataException("Responses 尚未完整完成；未保存片段譯文。");
    }

    internal static string ReadText(JsonElement response)
    {
        RequireCompleted(response);
        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Responses 回覆缺少 output 陣列。");
        var result = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("type", out var type)
                || type.GetString() != "message" || !item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("type", out var partType)
                    && partType.GetString() == "output_text" && part.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String) result.Append(text.GetString());
        }
        if (result.Length == 0) throw new InvalidDataException("Responses 未提供 output_text 譯文。");
        return result.ToString();
    }

    internal static string DescribeHttpFailure(HttpRequestException error)
    {
        var detail = error.StatusCode switch {
            HttpStatusCode.NotFound => "請檢查 url 的 /responses 路由及模型名稱",
            HttpStatusCode.Unauthorized => "請檢查 ApiKey",
            HttpStatusCode.Forbidden => "服務拒絕存取，請檢查密鑰或模型權限",
            HttpStatusCode.BadRequest => "服務不接受目前的模型、工具或請求參數",
            HttpStatusCode.TooManyRequests => "服務限流或額度不足",
            _ => "服務請求失敗"
        };
        return error.StatusCode.HasValue ? $"HTTP {(int)error.StatusCode.Value}，{detail}" : "無法連線至翻譯服務";
    }
}

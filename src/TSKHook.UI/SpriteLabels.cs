using System.Text.Json;
using Il2CppInterop.Runtime;
using Spine.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TSKHook.UI;

/// <summary>Verified sprite, help-image and Spine replacements; legacy label coordinates use a bottom-left origin.</summary>
internal static class SpriteLabels
{
    private const string OverlayName = "TSKHook.UI.SpriteLabel";
    private const string TextName = "TSKHook.UI.SpriteLabel.Text";
    private static Dictionary<string, LabelRule> rules = new(StringComparer.Ordinal);
    private static Dictionary<string, byte[]> replacementFiles = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, ReplacementAsset> replacementSprites = new();
    private static readonly Dictionary<int, ImageState> replacedImages = new();
    private static readonly Dictionary<(int Source, string File), Texture2D> replacementTextures = new();
    private static readonly Dictionary<int, SpineState> replacedSpines = new();
    private static readonly Dictionary<int, RawImageState> replacedRawImages = new();
    private static readonly Dictionary<int, OnlineCapture> pendingOnlineCaptures = new();
    private static readonly HashSet<string> failedOnlineImages = new(StringComparer.Ordinal);
    private static OnlineTranslationImages? onlineImages;
    private static Action<string> onlineLog = _ => { };

    // Exact main-menu assets and the game's gacha poster/banner families.
    // These stay original even when an older local rule or online cache exists.
    private static bool PreserveOriginalAsset(string name)
        => name is "btn_wepon_picturebook_SkeletonData" or "btn_maneuvers_SkeletonData"
            or "btn_belong_SkeletonData" or "btn_equip_SkeletonData"
            || name.StartsWith("m_gacha_", StringComparison.Ordinal)
            || name.StartsWith("m_common_gacha_", StringComparison.Ordinal)
            || name.StartsWith("page_bg_", StringComparison.Ordinal);

    internal static void ConfigureOnlineImages(OnlineTranslationImages? client, Action<string> log)
    {
        onlineImages = client;
        onlineLog = log;
        pendingOnlineCaptures.Clear();
        failedOnlineImages.Clear();
    }

    // Called once per Unity update. The HTTP worker never receives Unity objects.
    internal static void PumpOnlineImages(bool enabled)
    {
        if (!enabled || onlineImages == null) { pendingOnlineCaptures.Clear(); return; }
        if (pendingOnlineCaptures.Count == 0) return;
        var entry = pendingOnlineCaptures.First();
        pendingOnlineCaptures.Remove(entry.Key);
        var capture = entry.Value;
        if (capture.Graphic == null || !capture.Graphic.enabled
            || !capture.Graphic.gameObject.activeInHierarchy) return;
        var current = capture.Graphic switch {
            Image image => image.overrideSprite == capture.Sprite,
            RawImage image => image.texture == capture.Texture,
            SkeletonGraphic graphic => graphic.skeletonDataAsset == capture.Asset
                && graphic.mainTexture == capture.Texture,
            _ => false
        };
        if (!current || !onlineImages.CanQueue(capture.Name, capture.Width, capture.Height)) return;
        try
        {
            var crop = capture.Sprite == null
                ? new Rect(0, 0, capture.Width, capture.Height) : capture.Sprite.textureRect;
            var png = CaptureOnlinePng(capture.Texture, crop, capture.Sprite);
            onlineImages.Queue(capture.Name, capture.Graphic.gameObject.name, png, capture.Width, capture.Height);
        }
        catch (Exception)
        {
            if (failedOnlineImages.Add(capture.Name))
                onlineLog("圖片擷取失敗，保留原圖：" + capture.Name + "。F9 重新載入後可重試。");
        }
    }

    private static byte[] CaptureOnlinePng(Texture source, Rect crop, Sprite? sprite)
    {
        var width = (int)Math.Round(crop.width);
        var height = (int)Math.Round(crop.height);
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(width, height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D? readable = null;
        try
        {
            // Restrict readback to this UI sprite, never upload its entire atlas.
            Graphics.Blit(source, target, new Vector2((float)width / source.width, (float)height / source.height),
                new Vector2((float)Math.Round(crop.x) / source.width, (float)Math.Round(crop.y) / source.height));
            RenderTexture.active = target;
            readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readable.Apply(false, false);
            if (sprite != null) RestoreSpriteCanvas(readable, sprite, sprite.name);
            return EncodeOnlineCanvas(readable);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
    }

    private static byte[] EncodeOnlineCanvas(Texture2D source)
    {
        var layout = OnlineImageCanvas.ForSource(source.width, source.height);
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(layout.ContentWidth, layout.ContentHeight, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D? canvas = null;
        try
        {
            // The service only generates fixed canvas sizes. Enlarge this UI
            // image uniformly, retaining transparent padding and its full alpha.
            Graphics.Blit(source, target, Vector2.one, Vector2.zero);
            RenderTexture.active = target;
            canvas = new Texture2D(layout.CanvasWidth, layout.CanvasHeight, TextureFormat.RGBA32, false);
            canvas.SetPixels32(new Color32[layout.CanvasWidth * layout.CanvasHeight]);
            var bottom = layout.CanvasHeight - layout.Top - layout.ContentHeight;
            canvas.ReadPixels(new Rect(0, 0, layout.ContentWidth, layout.ContentHeight), layout.Left, bottom);
            canvas.Apply(false, false);
            return ImageConversion.EncodeToPNG(canvas).ToArray();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (canvas != null) UnityEngine.Object.Destroy(canvas);
        }
    }

    private static void RestoreOnlineCanvas(Texture2D texture, int width, int height, string file)
    {
        // Existing source-size caches (including manually corrected PNGs) stay
        // byte-for-byte unchanged. Other legacy crop sizes use the old validator.
        if (texture.width == width && texture.height == height) return;
        var layout = OnlineImageCanvas.ForSource(width, height);
        if (texture.width != layout.CanvasWidth || texture.height != layout.CanvasHeight) return;
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(width, height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        try
        {
            var bottom = layout.CanvasHeight - layout.Top - layout.ContentHeight;
            texture.filterMode = FilterMode.Bilinear;
            Graphics.Blit(texture, target,
                new Vector2((float)layout.ContentWidth / layout.CanvasWidth,
                    (float)layout.ContentHeight / layout.CanvasHeight),
                new Vector2((float)layout.Left / layout.CanvasWidth, (float)bottom / layout.CanvasHeight));
            RenderTexture.active = target;
            if (!texture.Reinitialize(width, height))
                throw new InvalidDataException("Could not restore the online image canvas: " + file);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply(false, false);
            // Persist the exact game-sized PNG so later loads need no canvas work.
            var png = ImageConversion.EncodeToPNG(texture).ToArray();
            File.WriteAllBytes(file, png);
            replacementFiles[file] = png;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }
    private static bool TryOnlineFile(string name, int width, int height, out string file)
    {
        file = "";
        if (onlineImages == null || failedOnlineImages.Contains(name)
            || !onlineImages.TryGet(name, width, height, out file)) return false;
        try
        {
            if (!replacementFiles.ContainsKey(file)) replacementFiles.Add(file, File.ReadAllBytes(file));
            return true;
        }
        catch (IOException)
        {
            if (failedOnlineImages.Add(name)) onlineLog("離線圖片無法讀取，保留原圖：" + name);
            return false;
        }
    }

    private static void RejectOnlineImage(string name)
    {
        if (failedOnlineImages.Add(name))
            onlineLog("離線圖片無法解碼或尺寸不符，保留原圖：" + name + "。請修正快取圖片後按 F9。");
    }

    private static bool CanUseOnline(string name, int width, int height)
        => onlineImages != null && !failedOnlineImages.Contains(name)
            && (onlineImages.TryGet(name, width, height, out _) || onlineImages.CanQueue(name, width, height));

    private static void QueueOnlineCapture(Graphic graphic, Texture texture, string name,
        Sprite? sprite = null, UnityEngine.Object? asset = null)
    {
        var width = sprite == null ? texture.width : (int)sprite.rect.width;
        var height = sprite == null ? texture.height : (int)sprite.rect.height;
        if (onlineImages == null || !graphic.gameObject.activeInHierarchy || failedOnlineImages.Contains(name)
            || !onlineImages.CanQueue(name, width, height)) return;
        pendingOnlineCaptures[graphic.GetInstanceID()] = new OnlineCapture(graphic, texture, name, width, height, sprite, asset);
    }

    internal static void Load(string path)
    {
        using var file = File.OpenRead(path);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, LabelRule>>(file,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Sprite labels must be a JSON object.");
        foreach (var name in loaded.Keys.Where(PreserveOriginalAsset).ToArray()) loaded.Remove(name);
        var directory = Path.GetFullPath(Path.GetDirectoryName(path)!) + Path.DirectorySeparatorChar;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var rule in loaded.Values)
        {
            if (string.IsNullOrEmpty(rule.Replacement) || files.ContainsKey(rule.Replacement)) continue;
            var replacementPath = Path.GetFullPath(Path.Combine(directory, rule.Replacement));
            if (!replacementPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Replacement images must be inside the UI plugin directory.");
            files.Add(rule.Replacement, File.ReadAllBytes(replacementPath));
        }
        // Restore live Images before releasing textures that they may still display.
        foreach (var state in replacedImages.Values) RestoreImage(state);
        replacedImages.Clear();
        foreach (var state in replacedSpines.Values) RestoreSpine(state);
        replacedSpines.Clear();
        SpineTitleMasks.Clear();
        foreach (var state in replacedRawImages.Values) RestoreRawImage(state);
        replacedRawImages.Clear();
        foreach (var asset in replacementSprites.Values)
        {
            UnityEngine.Object.Destroy(asset.Sprite);
            UnityEngine.Object.Destroy(asset.Texture);
        }
        replacementSprites.Clear();
        foreach (var texture in replacementTextures.Values) UnityEngine.Object.Destroy(texture);
        replacementTextures.Clear();
        pendingOnlineCaptures.Clear();
        failedOnlineImages.Clear();
        rules = new Dictionary<string, LabelRule>(loaded, StringComparer.Ordinal);
        replacementFiles = files;
    }

    internal static bool IsLabel(Component component)
        => component.gameObject.name == TextName || component.gameObject.name == OverlayName;

    internal static void Refresh(Image image, TMP_FontAsset font, bool enabled)
    {
        // The background Image of our label has no sprite and must not acquire another label.
        if (image.gameObject.name == OverlayName) return;
        var id = image.GetInstanceID();
        if (replacedImages.TryGetValue(id, out var state))
        {
            // Our setter hooks enqueue another refresh. The replacement and any
            // old label are already in place; skip native hierarchy work as well.
            if (image.m_OverrideSprite == state.Replacement && image.sprite == state.Base
                && enabled && image.enabled) return;
            RestoreImage(state);
            replacedImages.Remove(id);
        }
        var existing = image.transform.Find(OverlayName);
        var sprite = image.overrideSprite;
        if (!enabled || !image.enabled || sprite == null || PreserveOriginalAsset(sprite.name))
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }

        var isOnline = false;
        if (!rules.TryGetValue(sprite.name, out var rule))
        {
            isOnline = true;
            if (TryOnlineFile(sprite.name, (int)sprite.rect.width, (int)sprite.rect.height, out var file))
                rule = new LabelRule { Replacement = file };
            else
            {
                QueueOnlineCapture(image, sprite.texture, sprite.name, sprite);
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }
        }
        // The game has both 128x128 and 138x320 icon_base_random sprites.
        // Match the declared full canvas before decoding or replacing either.
        if ((rule.SourceWidth.HasValue && sprite.rect.width != rule.SourceWidth.Value)
            || (rule.SourceHeight.HasValue && sprite.rect.height != rule.SourceHeight.Value))
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }
        if (!string.IsNullOrEmpty(rule.Replacement))
        {
            Sprite replacement;
            try { replacement = GetReplacement(sprite, rule.Replacement, isOnline); }
            catch (Exception) when (isOnline)
            {
                RejectOnlineImage(sprite.name);
                return;
            }
            // The public overrideSprite getter falls back to sprite when its
            // backing field is null. Save that field so F11 restores it exactly.
            replacedImages[id] = new ImageState(image, image.sprite, image.m_OverrideSprite, replacement);
            image.overrideSprite = replacement;
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }
        if (font == null)
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }

        var parent = image.rectTransform.rect;
        var drawWidth = parent.width;
        var drawHeight = parent.height;
        if (image.preserveAspect && image.type == Image.Type.Simple)
        {
            var aspect = sprite.rect.width / sprite.rect.height;
            if (drawWidth / drawHeight > aspect) drawWidth = drawHeight * aspect;
            else drawHeight = drawWidth / aspect;
        }
        ApplyLabel(image, font, rule, drawWidth, drawHeight);
    }

    private static void RestoreImage(ImageState state)
    {
        // A reused Image may already have received a new override from the game.
        // Restore only our own override, leaving those game updates intact.
        if (state.Image != null && state.Image.m_OverrideSprite == state.Replacement)
            state.Image.overrideSprite = state.Override;
    }

    internal static bool HasRawImageRule(RawImage image)
        => replacedRawImages.ContainsKey(image.GetInstanceID())
            || (image.texture != null && ((rules.TryGetValue(image.texture.name, out var rule)
                && !string.IsNullOrEmpty(rule.Replacement))
                || CanUseOnline(image.texture.name, image.texture.width, image.texture.height)));

    internal static Texture GetOriginalTexture(RawImage image)
        => replacedRawImages.TryGetValue(image.GetInstanceID(), out var state)
            && image.texture == state.Replacement ? state.Original : image.texture;

    internal static void Refresh(RawImage image, bool enabled)
    {
        var id = image.GetInstanceID();
        if (replacedRawImages.TryGetValue(id, out var state))
        {
            if (enabled && image.enabled && image.texture == state.Replacement) return;
            RestoreRawImage(state);
            replacedRawImages.Remove(id);
        }
        var source = image.texture;
        if (!enabled || !image.enabled || source == null || PreserveOriginalAsset(source.name)) return;
        var isOnline = false;
        if (!rules.TryGetValue(source.name, out var rule))
        {
            isOnline = true;
            if (TryOnlineFile(source.name, source.width, source.height, out var file))
                rule = new LabelRule { Replacement = file };
            else { QueueOnlineCapture(image, source, source.name); return; }
        }
        if (string.IsNullOrEmpty(rule.Replacement)) return;
        Texture2D replacement;
        try { replacement = GetTextureReplacement(source, rule.Replacement, isOnline); }
        catch (Exception) when (isOnline)
        {
            RejectOnlineImage(source.name);
            return;
        }
        replacedRawImages[id] = new RawImageState(image, source, replacement);
        image.texture = replacement;
    }

    private static void RestoreRawImage(RawImageState state)
    {
        if (state.Image != null && state.Image.texture == state.Replacement)
            state.Image.texture = state.Original;
    }

    private static Sprite GetReplacement(Sprite source, string file, bool isOnline)
    {
        var id = source.GetInstanceID();
        if (replacementSprites.TryGetValue(id, out var cached)) return cached.Sprite;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            // Keep pixels readable for Image.alphaHitTestMinimumThreshold.
            if (!ImageConversion.LoadImage(texture, replacementFiles[file], false))
                throw new InvalidDataException("Could not decode replacement PNG: " + file);
            if (isOnline) RestoreOnlineCanvas(texture, (int)source.rect.width, (int)source.rect.height, file);
            RestoreSpriteCanvas(texture, source, file);
            texture.name = "TSKHook.UI." + file;
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            texture.filterMode = source.texture.filterMode;
            texture.wrapMode = TextureWrapMode.Clamp;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
            sprite.name = source.name + " [TSKHook.UI]";
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            // The PNG has the original full canvas, pivot and border. Its alpha
            // retains the visual outline; copying the atlas mesh is unnecessary
            // and the game's native runtime rejects that OverrideGeometry call.
            replacementSprites.Add(id, new ReplacementAsset(sprite, texture));
            return sprite;
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    private static void RestoreSpriteCanvas(Texture2D texture, Sprite source, string file)
    {
        var width = (int)source.rect.width;
        var height = (int)source.rect.height;
        var cropWidth = texture.width;
        var cropHeight = texture.height;
        if (cropWidth == width && cropHeight == height) return;
        var crop = source.textureRect;
        var offset = source.textureRectOffset;
        // Unity's tight mesh bounds may add a fractional texel to this offset.
        // Restore the exported pixel grid at its nearest original texel; never scale it.
        var left = (int)Math.Round(offset.x);
        var bottom = (int)Math.Round(offset.y);
        if (cropWidth != (int)Math.Round(crop.width) || cropHeight != (int)Math.Round(crop.height)
            || left < 0 || bottom < 0 || left + cropWidth > width || bottom + cropHeight > height)
            throw new InvalidDataException($"Replacement {file} must match the full sprite ({width} x {height}) or its cropped texture ({crop.width} x {crop.height}).");
        var pixels = texture.GetPixels32();
        var canvas = new Color32[width * height];
        for (var y = 0; y < cropHeight; y++)
            for (var x = 0; x < cropWidth; x++)
                canvas[(bottom + y) * width + left + x] = pixels[y * cropWidth + x];
        if (!texture.Reinitialize(width, height))
            throw new InvalidDataException("Could not restore the replacement sprite canvas: " + file);
        texture.SetPixels32(canvas);
        texture.Apply(false, false);
    }

    internal static bool HasSpineRule(SkeletonGraphic graphic)
    {
        var asset = graphic.skeletonDataAsset;
        return replacedSpines.ContainsKey(graphic.GetInstanceID())
            || (asset != null && (rules.ContainsKey(asset.name)
                || (graphic.mainTexture != null && CanUseOnline(asset.name, graphic.mainTexture.width, graphic.mainTexture.height))));
    }

    internal static void Refresh(SkeletonGraphic graphic, TMP_FontAsset font, bool enabled)
    {
        var existing = graphic.transform.Find(OverlayName);
        var asset = graphic.skeletonDataAsset;
        var id = graphic.GetInstanceID();
        if (replacedSpines.TryGetValue(id, out var state))
        {
            if (enabled && graphic.enabled && asset == state.Asset
                && graphic.allowMultipleCanvasRenderers == state.Multiple && HasSpineReplacement(state))
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }
            RestoreSpine(state);
            replacedSpines.Remove(id);
        }
        if (!enabled || !graphic.enabled || asset == null || PreserveOriginalAsset(asset.name))
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }
        var isOnline = false;
        if (!rules.TryGetValue(asset.name, out var rule))
        {
            isOnline = true;
            var original = graphic.mainTexture;
            if (original != null && TryOnlineFile(asset.name, original.width, original.height, out var file))
                rule = new LabelRule { Replacement = file };
            else
            {
                if (original != null) QueueOnlineCapture(graphic, original, asset.name, asset: asset);
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }
        }
        if (!string.IsNullOrEmpty(rule.Replacement))
        {
            var source = graphic.mainTexture;
            if (source == null) return;
            Texture2D replacement;
            try { replacement = GetTextureReplacement(source, rule.Replacement, isOnline); }
            catch (Exception) when (isOnline)
            {
                RejectOnlineImage(asset.name);
                return;
            }
            Texture? priorCustom = null;
            var hadCustom = graphic.CustomTextureOverride.TryGetValue(source, out priorCustom);
            replacedSpines[id] = new SpineState(graphic, asset, source, graphic.OverrideTexture,
                priorCustom, hadCustom, replacement, graphic.allowMultipleCanvasRenderers);
            if (graphic.allowMultipleCanvasRenderers) graphic.CustomTextureOverride[source] = replacement;
            else graphic.OverrideTexture = replacement;
            if (rule.ClipVertices != null) SpineTitleMasks.Track(graphic, rule.ClipVertices);
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }
        if (font == null)
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }
        var rect = graphic.rectTransform.rect;
        ApplyLabel(graphic, font, rule, rect.width, rect.height);
    }

    internal static Texture? GetOriginalTexture(SkeletonGraphic graphic)
        => replacedSpines.TryGetValue(graphic.GetInstanceID(), out var state)
            && HasSpineReplacement(state) ? state.Source : graphic.mainTexture;

    private static Texture2D GetTextureReplacement(Texture source, string file, bool isOnline)
    {
        var key = (source.GetInstanceID(), file);
        if (replacementTextures.TryGetValue(key, out var cached)) return cached;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, replacementFiles[file], false))
                throw new InvalidDataException("Could not decode replacement PNG: " + file);
            if (isOnline) RestoreOnlineCanvas(texture, source.width, source.height, file);
            if (texture.width != source.width || texture.height != source.height)
                throw new InvalidDataException($"Replacement texture {file} must be {source.width} x {source.height} pixels.");
            texture.name = "TSKHook.UI." + file;
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            texture.filterMode = source.filterMode;
            texture.wrapMode = source.wrapMode;
            replacementTextures.Add(key, texture);
            return texture;
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    private static bool HasSpineReplacement(SpineState state)
        => state.Multiple
            ? state.Graphic.CustomTextureOverride.TryGetValue(state.Source, out var current) && current == state.Replacement
            : state.Graphic.OverrideTexture == state.Replacement;

    private static void RestoreSpine(SpineState state)
    {
        if (state.Graphic != null) SpineTitleMasks.Restore(state.Graphic);
        if (state.Graphic == null || !HasSpineReplacement(state)) return;
        if (!state.Multiple) state.Graphic.OverrideTexture = state.Override;
        else if (state.HadCustomOverride) state.Graphic.CustomTextureOverride[state.Source] = state.CustomOverride!;
        else state.Graphic.CustomTextureOverride.Remove(state.Source);
    }

    private static void ApplyLabel(Graphic graphic, TMP_FontAsset font, LabelRule rule,
        float drawWidth, float drawHeight)
    {
        var parent = graphic.rectTransform.rect;
        if (parent.width <= 0 || parent.height <= 0) return;
        var existing = graphic.transform.Find(OverlayName);
        GameObject overlay;
        TextMeshProUGUI label;
        if (existing == null)
        {
            overlay = new GameObject(OverlayName, new[] { Il2CppType.Of<RectTransform>() });
            overlay.transform.SetParent(graphic.transform, false);
            var background = overlay.AddComponent<Image>();
            background.raycastTarget = false;
            var labelObject = new GameObject(TextName, new[] { Il2CppType.Of<RectTransform>() });
            labelObject.transform.SetParent(overlay.transform, false);
            label = labelObject.AddComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 6;
            var textRect = label.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }
        else
        {
            overlay = existing.gameObject;
            label = existing.Find(TextName).GetComponent<TextMeshProUGUI>();
        }
        overlay.layer = graphic.gameObject.layer;
        label.gameObject.layer = graphic.gameObject.layer;
        var left = (parent.width - drawWidth) / 2;
        var bottom = (parent.height - drawHeight) / 2;
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2((left + rule.X * drawWidth) / parent.width,
            (bottom + rule.Y * drawHeight) / parent.height);
        rect.anchorMax = new Vector2((left + (rule.X + rule.Width) * drawWidth) / parent.width,
            (bottom + (rule.Y + rule.Height) * drawHeight) / parent.height);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        ColorUtility.TryParseHtmlString(rule.Background, out var backgroundColor);
        ColorUtility.TryParseHtmlString(rule.Color, out var textColor);
        overlay.GetComponent<Image>().color = backgroundColor;
        label.font = font;
        label.color = textColor;
        label.fontSizeMax = rule.Height * drawHeight * 0.8f;
        label.fontSize = label.fontSizeMax;
        label.text = rule.Text;
        // Draw above the verified text region without taking pointer input.
        overlay.transform.SetAsLastSibling();
        overlay.SetActive(true);
    }

    internal static void RefreshAll(TMP_FontAsset font, bool enabled)
    {
        foreach (var item in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Image>()))
        {
            var image = item.Cast<Image>();
            if (image.gameObject.scene.IsValid()) Refresh(image, font, enabled);
        }
        foreach (var item in Resources.FindObjectsOfTypeAll(Il2CppType.Of<SkeletonGraphic>()))
        {
            var graphic = item.Cast<SkeletonGraphic>();
            if (graphic.gameObject.scene.IsValid() && HasSpineRule(graphic))
                Refresh(graphic, font, enabled);
        }
        foreach (var item in Resources.FindObjectsOfTypeAll(Il2CppType.Of<RawImage>()))
        {
            var image = item.Cast<RawImage>();
            if (image.gameObject.scene.IsValid() && HasRawImageRule(image)) Refresh(image, enabled);
        }
    }

    private sealed class LabelRule
    {
        public string Text { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string Background { get; set; } = "#FFFFFFFF";
        public string Color { get; set; } = "#000000FF";
        public string? Replacement { get; set; }
        public int? SourceWidth { get; set; }
        public int? SourceHeight { get; set; }
        public float[]? ClipVertices { get; set; }
    }

    private sealed record OnlineCapture(Graphic Graphic, Texture Texture, string Name,
        int Width, int Height, Sprite? Sprite, UnityEngine.Object? Asset);
    private sealed record ReplacementAsset(Sprite Sprite, Texture2D Texture);
    private sealed record ImageState(Image Image, Sprite? Base, Sprite? Override, Sprite Replacement);
    private sealed record RawImageState(RawImage Image, Texture Original, Texture2D Replacement);
    private sealed record SpineState(SkeletonGraphic Graphic, UnityEngine.Object Asset, Texture Source,
        Texture? Override, Texture? CustomOverride, bool HadCustomOverride, Texture2D Replacement, bool Multiple);
}

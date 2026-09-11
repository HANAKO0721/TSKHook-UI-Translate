using System.Text.Json;
using TMPro;
using UnityEngine;

namespace TSKHook.UI;

internal sealed class UiTranslation
{
    private readonly string directory;
    private readonly WorkspaceSync workspace;
    private TranslationCatalog catalog = null!;
    private OnlineTranslation? online;
    private OnlineTranslationImages? onlineImages;
    private readonly Dictionary<string, HashSet<string>> missing = new(StringComparer.Ordinal);
    private readonly Dictionary<int, TextState> originals = new();
    private readonly Dictionary<int, (TMP_FontAsset Font, Material Material, float LineSpacing, Material TranslatedMaterial, bool OwnsMaterial)> originalFonts = new();
    private readonly Dictionary<int, TMP_Text> pendingTexts = new();
    private readonly Dictionary<int, UnityEngine.UI.Image> pendingImages = new();
    private readonly Dictionary<int, UnityEngine.UI.RawImage> pendingRawImages = new();
    private readonly Dictionary<int, Spine.Unity.SkeletonGraphic> pendingSpineLabels = new();
    private readonly HashSet<int> capturedMasterIds = new();
    private readonly Dictionary<string, string[]> capturedMasters = new(StringComparer.Ordinal);
    private bool dirty;
    private bool mastersDirty;
    private bool namesDirty;
    private bool fontAttempted;
    private bool fontMetricsAligned;
    private bool namesAdded;
    private TMP_FontAsset? uiFont;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal UiTranslation(WorkspaceSync workspace)
    {
        this.workspace = workspace;
        directory = workspace.InstalledDirectory;
        var capturePath = Path.Combine(directory, "ui-missing.json");
        if (File.Exists(capturePath))
        {
            using var capture = JsonDocument.Parse(File.ReadAllText(capturePath));
            foreach (var entry in capture.RootElement.EnumerateArray())
            {
                var source = entry.GetProperty("source").GetString()!;
                missing[source] = entry.GetProperty("locations").EnumerateArray()
                    .Select(location => location.GetString()!).ToHashSet(StringComparer.Ordinal);
            }
        }
        // Mirror pending sources from an earlier run even if this run adds none.
        dirty = true;
    }

    internal void Reload()
    {
        var loaded = TranslationCatalog.Load(workspace.TranslationFiles());
        SpriteLabels.Load(workspace.TranslationFile("sprite-labels.json"));
        WebTranslations.Reload(workspace.TranslationFile("web-translations.json"),
            Path.Combine(directory, "web-missing.json"), workspace.WriteCapture);
        catalog = loaded;
        namesAdded = false;
        AddNames();
        onlineImages?.Dispose();
        online?.Dispose();
        var configPath = workspace.ConfigurationFile();
        online = new OnlineTranslation(directory, configPath, catalog.Entries, Plugin.Info);
        onlineImages = new OnlineTranslationImages(directory, configPath, catalog.Entries, Plugin.Info,
            online.StoreImageTranslations);
        SpriteLabels.ConfigureOnlineImages(onlineImages, Plugin.Info);
        WebTranslations.ConfigureOnline(online);
        WriteOfflineDictionary();
        foreach (var source in missing.Keys.ToArray())
        {
            if (catalog.TryTranslate(source, out _))
            {
                missing.Remove(source);
                dirty = true;
                continue;
            }
            var locations = missing[source];
            if (!locations.Any(IsCharacteristicDetailPath) || !catalog.TryTranslateLines(source, out _)) continue;
            // The same source can remain untranslated in a different view.
            locations.RemoveWhere(IsCharacteristicDetailPath);
            if (locations.Count == 0) missing.Remove(source);
            dirty = true;
        }
        workspace.ConsumeTranslationChanges();
        Plugin.Info("Traditional Chinese UI dictionary reloaded.");
    }

    internal void ProcessOnlineTranslations()
    {
        SpriteLabels.PumpOnlineImages(global::TSKHook.TSKConfig.TranslationEnabled);
        var textCompleted = online?.ConsumeCompleted() == true;
        var imageCompleted = onlineImages?.ConsumeCompleted() == true;
        if (!textCompleted && !imageCompleted) return;
        WebTranslations.RefreshOnlineCache();
        RefreshLoadedText();
    }

    internal void Dispose()
    {
        SpriteLabels.ConfigureOnlineImages(null, Plugin.Info);
        onlineImages?.Dispose();
        online?.Dispose();
    }

    private void WriteOfflineDictionary()
    {
        var web = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(workspace.TranslationFile("web-translations.json")));
        using var images = JsonDocument.Parse(File.ReadAllText(workspace.TranslationFile("sprite-labels.json")));
        var snapshot = new { ui = catalog.Entries, web, images = images.RootElement };
        workspace.WriteCapture("offline-dictionary.json", JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    internal void RefreshChangedTranslations()
    {
        if (!workspace.ConsumeTranslationChanges()) return;
        try
        {
            Reload();
            RefreshLoadedText();
        }
        catch (Exception error)
        {
            // An editor may temporarily save incomplete JSON. Keep the loaded
            // text catalog and retry when the file changes again or F9 is used.
            Plugin.Error(error);
        }
    }

    private void AddNames()
    {
        if (namesAdded || global::TSKHook.Translation.nameDicts.Count == 0) return;
        catalog.AddNames(global::TSKHook.Translation.nameDicts);
        namesAdded = true;
        namesDirty = true;
    }

    internal void EnsureFont()
    {
        if (fontAttempted) return;
        fontAttempted = true;
        if (global::TSKHook.Patch.TranslateFont == null || global::TSKHook.Patch.TMPTranslateFont == null)
        {
            var path = Path.Combine(BepInEx.Paths.PluginPath, "font", "notosanscjktc");
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) throw new IOException("Could not load Chinese font bundle: " + path);
            global::TSKHook.Patch.TranslateFont = bundle.LoadAsset("notosanscjktc").Cast<Font>();
            global::TSKHook.Patch.TMPTranslateFont = bundle.LoadAsset("notosanscjktc SDF").Cast<TMP_FontAsset>();
            bundle.Unload(false);
        }
        global::TSKHook.Patch.TranslateFont.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        global::TSKHook.Patch.TMPTranslateFont.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        // Build UI glyphs from the full font independently of the story atlas.
        uiFont = TMP_FontAsset.CreateFontAsset(global::TSKHook.Patch.TranslateFont, 32, 5,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        uiFont.name = "TSKHook UI Chinese";
        uiFont.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (!uiFont.TryAddCharacters("資料綁定角色覺醒強化裝備", false))
            Plugin.Info("Some UI glyphs could not be added to the dynamic font.");
        Plugin.Info("Chinese font initialized for UI.");
    }

    // TMP's getter clears the backing-string flag without updating m_text.
    // Read the current input directly so another getter cannot expose an old card name.
    internal static string ReadCurrentText(TMP_Text component)
    {
        if (component.m_inputSource != TMP_Text.TextInputSources.SetText
            && component.m_inputSource != TMP_Text.TextInputSources.SetTextArray)
            return component.m_text ?? string.Empty;

        var backing = component.m_TextBackingArray;
        var source = backing.m_Array;
        var characters = new char[backing.m_Count];
        for (var i = 0; i < characters.Length; i++)
        {
            var character = (char)source[i];
            if (character == 0) break;
            characters[i] = character;
        }
        return new string(characters);
    }

    internal void Translate(TMP_Text component)
    {
        var before = ReadCurrentText(component);
        var after = Resolve(component, before, out var known);
        var id = component.GetInstanceID();
        if (known && uiFont != null && component.font != uiFont)
        {
            AlignUiFontMetrics(component.font);
            if (component.font != null)
            {
                var sourceMaterial = component.fontSharedMaterial ?? component.font.material ?? uiFont.material;
                var ownsMaterial = IsStyledOptionLabel(component);
                // Acquire first: a rebound label may reuse its current pooled material.
                var material = CreateTranslatedMaterial(sourceMaterial, ownsMaterial);
                if (originalFonts.TryGetValue(id, out var previous))
                    ReleaseTranslatedMaterial(previous.TranslatedMaterial, previous.OwnsMaterial);
                originalFonts[id] = (component.font, sourceMaterial, component.lineSpacing, material, ownsMaterial);
            }
            component.font = uiFont;
            if (originalFonts.TryGetValue(id, out var initialStyle))
                component.fontSharedMaterial = initialStyle.TranslatedMaterial;
        }
        else if (!known && originalFonts.TryGetValue(id, out var originalFont))
        {
            component.font = originalFont.Font;
            component.fontSharedMaterial = originalFont.Material;
            component.lineSpacing = originalFont.LineSpacing;
            ReleaseTranslatedMaterial(originalFont.TranslatedMaterial, originalFont.OwnsMaterial);
            originalFonts.Remove(id);
        }
        // The UI font now shares the original line metrics, so preserve the
        // game's per-label spacing (including its deliberate negative spacing).
        if (known && uiFont != null && originalFonts.TryGetValue(id, out var style))
        {
            var current = component.fontSharedMaterial;
            if (current != null && current != style.TranslatedMaterial
                && current.mainTexture != uiFont.material.mainTexture)
            {
                // Tabs and radio buttons change their preset without changing font.
                // Save that game preset for F11 and translate its current selection color.
                var material = CreateTranslatedMaterial(current, style.OwnsMaterial);
                originalFonts[id] = (style.Font, current, style.LineSpacing, material, style.OwnsMaterial);
                component.fontSharedMaterial = material;
                ReleaseTranslatedMaterial(style.TranslatedMaterial, style.OwnsMaterial);
            }
            else if (current == null || (style.OwnsMaterial && current != style.TranslatedMaterial))
                component.fontSharedMaterial = style.TranslatedMaterial;
        }
        if (after != before) component.text = after;
    }

    private void AlignUiFontMetrics(TMP_FontAsset? source)
    {
        if (fontMetricsAligned || source == null || source.name != "FOT-RodinNTLGPro-B SDF") return;
        // The bundled Noto Bold uses hhea 1.48-em vertical metrics. In the
        // game's 30px story rows this forced 25pt text down to 20.25pt. Keep
        // Noto's glyphs and weight, but match the original UI layout metrics.
        var original = source.faceInfo;
        var chinese = uiFont!.faceInfo;
        var ratio = (float)chinese.pointSize / original.pointSize;
        chinese.lineHeight = original.lineHeight * ratio;
        chinese.ascentLine = original.ascentLine * ratio;
        chinese.descentLine = original.descentLine * ratio;
        uiFont.faceInfo = chinese;
        fontMetricsAligned = true;
        Plugin.Info($"UI font layout aligned to {source.name}: {chinese.lineHeight}/{chinese.ascentLine}/{chinese.descentLine} at {chinese.pointSize}pt; original font unchanged.");
    }

    private static bool IsStyledOptionLabel(TMP_Text text)
    {
        for (var parent = text.transform.parent; parent != null; parent = parent.parent)
        {
            if (parent.name == "TabRoot") return true;
            if (parent.name == "Body"
                && parent.parent?.name == "Window"
                && parent.parent.parent?.name == "PictureBookPopupCharaDisplaySettingView(Clone)")
                return true;
        }
        return false;
    }

    private Material CreateTranslatedMaterial(Material source, bool isOption)
    {
        if (!isOption)
        {
            var pooled = TMP_MaterialManager.GetFallbackMaterial(source, uiFont!.material);
            TMP_MaterialManager.AddFallbackMaterialReference(pooled);
            return pooled;
        }

        // Copy the preset directly. Borrowing and immediately releasing a pooled
        // template for each option queues that same fallback for cleanup repeatedly.
        var material = new Material(uiFont!.material)
        {
            name = source.name + " [TSK UI Option]",
            hideFlags = HideFlags.HideAndDontSave
        };
        TMP_MaterialManager.CopyMaterialPresetProperties(source, material);
        material.SetFloat("_FaceDilate", 0);
        material.SetFloat("_OutlineWidth", Math.Min(material.GetFloat("_OutlineWidth"), 0.12f));
        return material;
    }

    private static void ReleaseTranslatedMaterial(Material material, bool owned)
    {
        if (owned) UnityEngine.Object.Destroy(material);
        else TMP_MaterialManager.ReleaseFallbackMaterial(material);
    }

    internal string Translate(UnityEngine.UI.Text component, string value)
    {
        CaptureActiveTextMaster();
        var result = Resolve(component, value, out var known);
        if (known && global::TSKHook.Patch.TranslateFont != null)
            component.font = global::TSKHook.Patch.TranslateFont;
        return result;
    }

    internal static bool IsProtectedInputText(Component component)
    {
        // The declared placeholder is a system hint; actual input keeps priority.
        var tmpInput = component.GetComponentInParent<TMP_InputField>();
        if (tmpInput != null)
            return component == tmpInput.textComponent || component != tmpInput.placeholder;
        var input = component.GetComponentInParent<UnityEngine.UI.InputField>();
        return input != null && (component == input.textComponent || component != input.placeholder);
    }
    private string Resolve(Component component, string value, out bool known)
    {
        known = false;
        if (SpriteLabels.IsLabel(component)) return value;
        if (string.IsNullOrEmpty(value)) return value;
        AddNames();
        var id = component.GetInstanceID();
        var source = value;
        if (originals.TryGetValue(id, out var saved) && saved.Result == value) source = saved.Source;
        if (!global::TSKHook.TSKConfig.TranslationEnabled) return source;
        var componentPath = GetPath(component.transform);
        var insideInput = IsProtectedInputText(component);
        if (UiTranslationPolicy.PreserveOriginal(componentPath, insideInput)) return source;
        var matched = catalog.TryTranslate(source, out var result);
        // These characteristic panels join independent, complete descriptions.
        if (!matched && component.name == "SkilDetailText"
            && IsCharacteristicDetailPath(GetPath(component.transform)))
            matched = catalog.TryTranslateLines(source, out result);
        if (!matched && online != null) matched = online.TryGet(source, out result);
        if (matched)
        {
            known = true;
            originals[id] = new TextState(source, result);
            return result;
        }
        if (HasJapaneseText(source))
        {
            if (component.gameObject.activeInHierarchy) online?.Queue(source, componentPath);
            if (!missing.TryGetValue(source, out var places))
            {
                places = new HashSet<string>(StringComparer.Ordinal);
                missing.Add(source, places);
                dirty = true;
            }
            if (places.Count < 3 && places.Add(GetPath(component.transform))) dirty = true;
        }
        return source;
    }

    private static bool HasJapaneseText(string value)
        => value.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff');

    internal void CaptureSource(string source, string location)
    {
        if (string.IsNullOrEmpty(source) || !HasJapaneseText(source) || catalog.TryTranslate(source, out _)) return;
        if (!missing.TryGetValue(source, out var places))
        {
            places = new HashSet<string>(StringComparer.Ordinal);
            missing.Add(source, places);
        }
        if (places.Add(location)) dirty = true;
    }

    private static bool IsCharacteristicDetailPath(string path)
        => path.EndsWith("/SkilDetailText", StringComparison.Ordinal)
            && (path.Contains("/PopupCharacteristicDetail(Clone)/", StringComparison.Ordinal)
                || path.EndsWith("/PopupEnemyDetail(Clone)/Window/Body/SkillGroup/SkillAndCharacteristic/ CharacteristicRoot/ CharacteristicDetail/Viewport/SkilDetailText", StringComparison.Ordinal));

    internal static string GetPath(Transform transform)
    {
        var names = new List<string>();
        for (var current = transform; current != null; current = current.parent) names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    internal void RefreshLoadedText()
    {
        CaptureActiveTextMaster();
        foreach (var text in FindAll<TMP_Text>())
        {
            if (!text.gameObject.scene.IsValid() || !text.gameObject.activeInHierarchy) continue;
            Translate(text);
            text.SetAllDirty();
        }
        foreach (var text in FindAll<UnityEngine.UI.Text>())
        {
            if (!text.gameObject.scene.IsValid() || !text.gameObject.activeInHierarchy) continue;
            var result = Translate(text, text.text);
            if (result != text.text) text.text = result;
        }
        SpriteLabels.RefreshAll(uiFont!, global::TSKHook.TSKConfig.TranslationEnabled);
    }

    internal void RefreshSprite(UnityEngine.UI.Image image, bool enabled = true)
        => SpriteLabels.Refresh(image, uiFont!, enabled && global::TSKHook.TSKConfig.TranslationEnabled);

    internal void RefreshSpineLabel(Spine.Unity.SkeletonGraphic graphic, bool enabled = true)
        => SpriteLabels.Refresh(graphic, uiFont!, enabled && global::TSKHook.TSKConfig.TranslationEnabled);

    internal void RefreshRawImage(UnityEngine.UI.RawImage image, bool enabled = true)
        => SpriteLabels.Refresh(image, enabled && global::TSKHook.TSKConfig.TranslationEnabled);

    internal void QueueRawImage(UnityEngine.UI.RawImage image)
        => pendingRawImages[image.GetInstanceID()] = image;

    internal void QueueSpineLabel(Spine.Unity.SkeletonGraphic graphic)
    {
        if (SpriteLabels.HasSpineRule(graphic))
            pendingSpineLabels[graphic.GetInstanceID()] = graphic;
    }

    internal void QueueText(TMP_Text text)
    {
        if (!SpriteLabels.IsLabel(text))
            pendingTexts[text.GetInstanceID()] = text;
    }

    internal void ReleaseText(TMP_Text text)
    {
        var id = text.GetInstanceID();
        if (originalFonts.Remove(id, out var style))
            ReleaseTranslatedMaterial(style.TranslatedMaterial, style.OwnsMaterial);
        pendingTexts.Remove(id);
        originals.Remove(id);
    }

    internal void QueueSprite(UnityEngine.UI.Image image)
    {
        if (!SpriteLabels.IsLabel(image))
            pendingImages[image.GetInstanceID()] = image;
    }

    // Changing a font inside ParseInputText leaves the current render pass with
    // material references from the old atlas. Apply changes before the next pass.
    internal void ProcessQueuedUi()
    {
        var texts = pendingTexts.Values.ToArray();
        pendingTexts.Clear();
        if (texts.Length != 0) CaptureActiveTextMaster();
        foreach (var text in texts)
            if (text != null && text.gameObject.scene.IsValid() && text.gameObject.activeInHierarchy)
                Translate(text);
        var images = pendingImages.Values.ToArray();
        pendingImages.Clear();
        foreach (var image in images)
            if (image != null && image.gameObject.scene.IsValid() && image.gameObject.activeInHierarchy)
                RefreshSprite(image);
        var rawImages = pendingRawImages.Values.ToArray();
        pendingRawImages.Clear();
        foreach (var image in rawImages)
            if (image != null && image.gameObject.scene.IsValid() && image.gameObject.activeInHierarchy)
                RefreshRawImage(image);
        var spineLabels = pendingSpineLabels.Values.ToArray();
        pendingSpineLabels.Clear();
        foreach (var graphic in spineLabels)
            if (graphic != null && graphic.gameObject.scene.IsValid() && graphic.gameObject.activeInHierarchy)
                RefreshSpineLabel(graphic);
    }

    internal void CaptureLoadedText()
    {
        WebTranslations.Capture();
        RefreshLoadedText();
        foreach (var master in FindAll<TextMaster>())
            CaptureMaster(master);
        namesDirty = true;
        Plugin.Info("Unity asset cache: " + Caching.defaultCache.path);
        Plugin.Info("UI font still available: " + (uiFont != null));
        var fonts = new List<object>();
        foreach (var text in FindAll<TMP_Text>())
            if (text.gameObject.scene.IsValid() && text.gameObject.activeInHierarchy)
                fonts.Add(new { type = "TMP", path = GetPath(text.transform), text = ReadCurrentText(text),
                    font = text.font?.name, uiFont = uiFont?.name,
                    material = text.fontSharedMaterial?.name,
                    atlas = text.fontSharedMaterial?.mainTexture?.name,
                    materialId = text.fontSharedMaterial?.GetInstanceID(),
                    atlasId = text.fontSharedMaterial?.mainTexture?.GetInstanceID(),
                    uiAtlasId = uiFont?.material?.mainTexture?.GetInstanceID(),
                    gradientScale = text.fontSharedMaterial != null && text.fontSharedMaterial.HasProperty("_GradientScale")
                        ? text.fontSharedMaterial.GetFloat("_GradientScale") : 0,
                    uiGradientScale = uiFont?.material?.GetFloat("_GradientScale"),
                    outlineWidth = text.fontSharedMaterial != null && text.fontSharedMaterial.HasProperty("_OutlineWidth")
                        ? text.fontSharedMaterial.GetFloat("_OutlineWidth") : 0,
                    faceDilate = text.fontSharedMaterial != null && text.fontSharedMaterial.HasProperty("_FaceDilate")
                        ? text.fontSharedMaterial.GetFloat("_FaceDilate") : 0,
                    materialCount = text.textInfo?.materialCount,
                    subMeshes = text.GetComponentsInChildren<TMP_SubMeshUI>().Select(sub => new {
                        name = sub.name, shared = DescribeMaterial(sub.sharedMaterial),
                        fallback = DescribeMaterial(sub.fallbackMaterial) }).ToArray(),
                    fontSize = text.fontSize, autoSizing = text.enableAutoSizing, fontSizeMin = text.fontSizeMin, fontSizeMax = text.fontSizeMax,
                    metrics = DescribeFontMetrics(text.font),
                    sourceMetrics = DescribeFontMetrics(originalFonts.TryGetValue(text.GetInstanceID(), out var sourceStyle) ? sourceStyle.Font : text.font),
                    rectWidth = text.rectTransform.rect.width, rectHeight = text.rectTransform.rect.height,
                    preferredWidth = text.preferredWidth, preferredHeight = text.preferredHeight,
                    fontStyle = text.fontStyle.ToString(), wordWrapping = text.enableWordWrapping, overflowMode = text.overflowMode.ToString(), lineSpacing = text.lineSpacing,
                    lineHeight = text.font?.faceInfo.lineHeight });
        foreach (var text in FindAll<UnityEngine.UI.Text>())
            if (text.gameObject.activeInHierarchy)
                fonts.Add(new { type = "UGUI", path = GetPath(text.transform), text = text.text,
                    font = text.font?.name, dynamic = text.font != null && text.font.dynamic,
                    hasBinding = text.font != null && text.font.HasCharacter('\u7d81') });
        workspace.WriteCapture("ui-fonts.json", JsonSerializer.Serialize(fonts, JsonOptions));
        var sprites = new List<object>();
        foreach (var image in FindAll<UnityEngine.UI.Image>())
            if (image.sprite != null && image.gameObject.activeInHierarchy)
            {
                var path = GetPath(image.transform);
                sprites.Add(new { path, sprite = image.sprite.name,
                    activeSprite = image.overrideSprite?.name, image.enabled,
                    scene = image.gameObject.scene.name, sceneValid = image.gameObject.scene.IsValid(),
                    width = image.sprite.rect.width, height = image.sprite.rect.height,
                    rectWidth = image.rectTransform.rect.width, rectHeight = image.rectTransform.rect.height });
                if (path.Contains("PopupSubTutorialView(Clone)/Window/Body/tutorialImage", StringComparison.Ordinal))
                    TextureCapture.Save(image.sprite.texture, workspace, image.sprite.name);
            }
        workspace.WriteCapture("ui-sprites.json", JsonSerializer.Serialize(sprites, JsonOptions));
        var rawImages = new List<object>();
        foreach (var image in FindAll<UnityEngine.UI.RawImage>())
        {
            if (!image.gameObject.scene.IsValid() || !image.gameObject.activeInHierarchy || image.texture == null) continue;
            var path = GetPath(image.transform);
            var source = SpriteLabels.GetOriginalTexture(image);
            rawImages.Add(new { path, texture = source.name, activeTexture = image.texture.name, width = source.width,
                height = source.height, image.enabled });
            if (path.Contains("Help", StringComparison.Ordinal))
                TextureCapture.Save(source, workspace);
        }
        workspace.WriteCapture("ui-raw-images.json", JsonSerializer.Serialize(rawImages, JsonOptions));
        UiAssetCapture.Capture(workspace);
        Flush();
        Plugin.Info($"Captured UI sources: {missing.Count} untranslated strings, {sprites.Count} visible sprites.");
    }

    internal void Flush()
    {
        if (dirty)
        {
            var entries = missing.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new { source = p.Key, locations = p.Value.ToArray() });
            workspace.WriteCapture("ui-missing.json", JsonSerializer.Serialize(entries, JsonOptions));
            dirty = false;
        }
        if (mastersDirty)
        {
            workspace.WriteCapture("text-masters.json", JsonSerializer.Serialize(capturedMasters, JsonOptions));
            mastersDirty = false;
        }
        if (namesDirty)
        {
            workspace.WriteCapture("existing-names.json",
                JsonSerializer.Serialize(global::TSKHook.Translation.nameDicts, JsonOptions));
            namesDirty = false;
        }
    }

    private void CaptureActiveTextMaster()
    {
        // Read the service already used by the game's UI. No asset loading,
        // global polling, or hooks on IL2CPP's shared field setters are needed.
        var master = TextManager.service?.TryCast<TextServiceBase>()?.master;
        if (master != null) CaptureMaster(master);
    }

    private void CaptureMaster(TextMaster master)
    {
        var id = master.GetInstanceID();
        if (capturedMasterIds.Contains(id) || master.data == null) return;
        var sources = master.data.ToArray();
        if (sources.Length == 0) return;
        capturedMasterIds.Add(id);
        capturedMasters[master.name] = sources;
        for (var i = 0; i < sources.Length; i++)
            CaptureSource(sources[i], $"TextMaster/{master.name}/{i}");
        mastersDirty = true;
    }

    private static object? DescribeFontMetrics(TMP_FontAsset? font)
    {
        if (font == null) return null;
        var face = font.faceInfo;
        return new { face.pointSize, face.scale, face.lineHeight, face.ascentLine,
            face.descentLine, face.capLine, face.meanLine, face.baseline };
    }

    private static object? DescribeMaterial(Material? material)
    {
        if (material == null) return null;
        return new { id = material.GetInstanceID(), atlasId = material.mainTexture?.GetInstanceID(),
            gradientScale = material.HasProperty("_GradientScale") ? material.GetFloat("_GradientScale") : 0,
            outlineWidth = material.HasProperty("_OutlineWidth") ? material.GetFloat("_OutlineWidth") : 0 };
    }

    private static IEnumerable<T> FindAll<T>() where T : UnityEngine.Object
        => Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<T>())
            .Select(value => value.Cast<T>());

    private sealed record TextState(string Source, string Result);
}

using System.Text.Json;
using Il2CppInterop.Runtime;
using Spine.Unity;
using UnityEngine;

namespace TSKHook.UI;

// F4 saves the game's loaded UI atlas coordinates and original textures for typesetting.
internal static class UiAssetCapture
{
    internal static void Capture(WorkspaceSync workspace)
    {
        SpriteViewCapture.Capture(workspace);
        var records = new List<object>();
        var exported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Resources.FindObjectsOfTypeAll(Il2CppType.Of<SkeletonGraphic>()))
        {
            var graphic = item.Cast<SkeletonGraphic>();
            var asset = graphic.skeletonDataAsset;
            if (!graphic.gameObject.scene.IsValid() || !graphic.gameObject.activeInHierarchy
                || asset == null || !asset.name.StartsWith("btn_", StringComparison.Ordinal)) continue;
            var source = SpriteLabels.GetOriginalTexture(graphic);
            records.Add(new { skeleton = asset.name, texture = source?.name,
                width = source?.width, height = source?.height, graphic.allowMultipleCanvasRenderers,
                skeletonFile = asset.skeletonJSON?.name, skeletonScale = asset.scale,
                titleSweepMask = SpineTitleMasks.CaptureState(graphic) });
            if (!exported.Add(asset.name)) continue;
            // Inspect the original geometry of active translated buttons beside
            // their atlas, including title sweep masks; never alter loaded data.
            if (SpriteLabels.HasSpineRule(graphic))
            {
                var skeletonFile = asset.skeletonJSON;
                if (skeletonFile != null)
                    workspace.WriteCapture(Path.Combine("skeletons", skeletonFile.name), skeletonFile.bytes.ToArray());
            }
            if (source != null) TextureCapture.Save(source, workspace, asset.name);
            if (asset.atlasAssets == null) continue;
            foreach (var atlasBase in asset.atlasAssets)
            {
                var atlas = atlasBase?.TryCast<SpineAtlasAsset>();
                if (atlas?.atlasFile != null)
                    workspace.WriteCapture(Path.Combine("atlas", asset.name + ".atlas.txt"), atlas.atlasFile.text);
            }
        }
        workspace.WriteCapture("ui-spine-images.json", JsonSerializer.Serialize(records,
            new JsonSerializerOptions { WriteIndented = true }));
        // Keep targeted image capture editable while the game is running.
        var requestPath = workspace.TranslationFile("capture-sprites.json");
        var requested = File.Exists(requestPath)
            ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(requestPath)) ?? Array.Empty<string>()
            : Array.Empty<string>();
        var requestedNames = requested.ToHashSet(StringComparer.Ordinal);
        foreach (var item in Resources.FindObjectsOfTypeAll(Il2CppType.Of<UnityEngine.UI.Image>()))
        {
            var image = item.Cast<UnityEngine.UI.Image>();
            var sprite = image.sprite;
            if (!image.gameObject.scene.IsValid() || !image.gameObject.activeInHierarchy
                || sprite == null || !requestedNames.Contains(sprite.name)) continue;
            if (!exported.Add(sprite.name)) continue;
            var crop = sprite.textureRect;
            var offset = sprite.textureRectOffset;
            TextureCapture.Save(sprite.texture, workspace, sprite.name + "_sheet");
            workspace.WriteCapture(sprite.name.Replace('_', '-') + "-sprite.json", JsonSerializer.Serialize(new
            {
                texture = sprite.texture.name, textureWidth = sprite.texture.width,
                textureHeight = sprite.texture.height, fullWidth = sprite.rect.width,
                fullHeight = sprite.rect.height, x = crop.x, y = crop.y,
                width = crop.width, height = crop.height, offsetX = offset.x, offsetY = offset.y,
                rotation = sprite.packingRotation.ToString()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

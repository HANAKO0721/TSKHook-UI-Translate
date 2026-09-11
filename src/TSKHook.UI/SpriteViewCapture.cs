using System.Text.Json;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace TSKHook.UI;

// F4-only state for UI lists with observed missing images.
internal static class SpriteViewCapture
{
    internal static void Capture(WorkspaceSync workspace)
    {
        var views = new List<object>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Resources.FindObjectsOfTypeAll(Il2CppType.Of<global::SpriteView>()))
        {
            var view = item.Cast<global::SpriteView>();
            if (view == null || !view.gameObject.scene.IsValid()) continue;
            var path = UiTranslation.GetPath(view.transform);
            if (!path.Contains("/PictureBookAlbumTopView(Clone)/", StringComparison.Ordinal)
                && !(path.Contains("/PictureBookEquipView(Clone)/", StringComparison.Ordinal)
                    && path.Contains("/EquipCommonIconVIew/", StringComparison.Ordinal))
                && !path.Contains("/PopupSudueEventRewardListView(Clone)/", StringComparison.Ordinal)
                && !path.Contains("/PopupFinisTotalReward(Clone)/", StringComparison.Ordinal)) continue;

            var loadingPath = view.loadingPath;
            var cts = view.cts;
            if (!string.IsNullOrEmpty(loadingPath)) paths.Add(loadingPath);
            views.Add(new
            {
                path, instanceId = view.GetInstanceID(), scene = view.gameObject.scene.name,
                view.gameObject.activeSelf, view.gameObject.activeInHierarchy,
                loadingPath, ctsNull = cts == null,
                ctsIsCancellationRequested = cts == null ? (bool?)null : cts.IsCancellationRequested,
                image = DescribeImage(view.renderComp)
            });
        }

        var assets = new List<object>();
        if (paths.Count > 0)
        {
            // Read the existing loader; do not initialize a new loader or request assets.
            var loader = global::AddressableWrapper<Sprite>.loader;
            var concrete = loader?.TryCast<global::AddressableWrapper<Sprite>.AddressableLoader>();
            var cache = loader?.GetCache();
            foreach (var path in paths.OrderBy(path => path, StringComparer.Ordinal))
            {
                var match = DelegateSupport.ConvertDelegate<Il2CppSystem.Predicate<global::IDisposableAsset<Sprite>>>(
                    new Predicate<global::IDisposableAsset<Sprite>>(entry => entry != null && entry.Path == path));
                var asset = cache?.Find(match);
                assets.Add(new
                {
                    path, loaderAvailable = loader != null,
                    pending = concrete?.loadingPath?.Contains(path), cacheFound = asset != null,
                    referenceCount = asset?.ReferenceCount, assetIsCanceled = asset?.IsCanceled,
                    sprite = asset == null ? null : DescribeSprite(asset.Value)
                });
            }
        }
        workspace.WriteCapture("ui-sprite-view-state.json", JsonSerializer.Serialize(new
        {
            capturedAtUtc = DateTime.UtcNow.ToString("O"), views, assets
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object DescribeImage(Image image)
    {
        if (image == null) return new { rawNull = ReferenceEquals(image, null), unityAlive = false };
        return new
        {
            rawNull = false, unityAlive = true, image.enabled,
            image.gameObject.activeSelf, image.gameObject.activeInHierarchy,
            sprite = DescribeSprite(image.sprite)
        };
    }

    private static object DescribeSprite(Sprite sprite)
        => new { rawNull = ReferenceEquals(sprite, null), unityAlive = sprite != null,
            name = sprite != null ? sprite.name : null };
}

using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using Cts = Il2CppSystem.Threading.CancellationTokenSource;

namespace TSKHook.UI;

internal static class SpriteViewLoadPatches
{
    // Only excludes this synchronous retry invocation from acquiring another retry.
    private static IntPtr retryingView;

    [HarmonyPrefix, HarmonyPatch(typeof(global::SpriteView), "UpdateRender")]
    internal static void BeforeUpdateRender(global::SpriteView __instance, string path, bool keepActive, out bool __state)
    {
        __state = false;
        if (__instance.Pointer == retryingView || string.IsNullOrEmpty(path) || path == __instance.loadingPath) return;
        var loader = global::AddressableWrapper<Sprite>.loader?
            .TryCast<global::AddressableWrapper<Sprite>.AddressableLoader>();
        __state = loader != null && loader.loadingPath.Contains(path);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(global::SpriteView), "UpdateRender")]
    internal static void AfterUpdateRender(global::SpriteView __instance, string path, bool keepActive,
        bool __state, ref UniTask __result)
    {
        var cts = __instance.cts;
        if (!__state || cts == null) return;
        var original = __result;
        var completion = new UniTaskCompletionSource();
        __result = completion.Task;
        Plugin.Behaviour.StartCoroutine(Complete(original, __instance, path, keepActive, cts, completion).WrapToIl2Cpp());
    }

    private static IEnumerator Complete(UniTask original, global::SpriteView view, string path,
        bool keepActive, Cts requestCts, UniTaskCompletionSource completion)
    {
        var failed = false;
        var onError = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Exception>>(
            new Action<Il2CppSystem.Exception>(error => { failed = true; completion.TrySetException(error); }));
        var originalSteps = UniTaskExtensions.ToCoroutine(original, onError);
        while (originalSteps.MoveNext()) yield return originalSteps.Current;
        if (failed) yield break;

        if (NeedsRetry(view, path, requestCts))
        {
            // The shared wait returned no asset, so this path owns no lease to release.
            // Re-enter the native method to retain its CTS, image and cache ownership rules.
            view.loadingPath = string.Empty;
            UniTask retry;
            var previous = retryingView;
            retryingView = view.Pointer;
            try { retry = view.UpdateRender(path, keepActive); }
            finally { retryingView = previous; }
            var retrySteps = UniTaskExtensions.ToCoroutine(retry, onError);
            while (retrySteps.MoveNext()) yield return retrySteps.Current;
        }
        if (!failed) completion.TrySetResult();
    }

    private static bool NeedsRetry(global::SpriteView view, string path, Cts requestCts)
    {
        if (view == null || view.loadingPath != path || requestCts.IsCancellationRequested) return false;
        var currentCts = view.cts;
        if (currentCts == null || currentCts.Pointer != requestCts.Pointer) return false;
        var cache = global::AddressableWrapper<Sprite>.loader?.GetCache();
        if (cache == null) return false;
        var match = DelegateSupport.ConvertDelegate<Il2CppSystem.Predicate<global::IDisposableAsset<Sprite>>>(
            new Predicate<global::IDisposableAsset<Sprite>>(asset => asset != null && asset.Path == path));
        return cache.Find(match) == null;
    }
}

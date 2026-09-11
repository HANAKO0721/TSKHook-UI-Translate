using HarmonyLib;

namespace TSKHook.UI;

internal static class SkeletonGraphicLifecyclePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(global::SkeletonGraphicView), "Unload")]
    internal static void BeforeUnload(global::SkeletonGraphicView __instance, out bool __state)
        => __state = __instance.dispose != null && __instance.cts == null;

    [HarmonyPostfix, HarmonyPatch(typeof(global::SkeletonGraphicView), "Unload")]
    internal static void AfterUnload(global::SkeletonGraphicView __instance, bool __state)
    {
        // Unload has already released the completed asset through its callback.
        // A pending replacement's savePath belongs to the new request instead.
        if (__state) __instance.savePath = string.Empty;
    }
}

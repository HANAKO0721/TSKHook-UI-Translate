using HarmonyLib;

namespace TSKHook.UI;

internal static class RaycastMaskLifecyclePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(global::RaycastMask), "IsRaycastLocationValid")]
    internal static bool IsRaycastLocationValid(global::RaycastMask __instance, ref bool __result)
    {
        // The game samples Image.sprite on every raycast, even during an asset
        // transition. An unavailable base sprite has no pixels that can be hit.
        var image = __instance._image;
        var sprite = image != null ? image.sprite : null;
        if (sprite != null && sprite.texture != null) return true;
        __result = false;
        return false;
    }
}

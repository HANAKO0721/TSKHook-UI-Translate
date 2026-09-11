using HarmonyLib;
using TMPro;

namespace TSKHook.UI;

internal static class TmpSubMeshLifecyclePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(TMP_SubMeshUI), "UpdateMaterial")]
    internal static bool UpdateMaterial(TMP_SubMeshUI __instance)
    {
        // Mask/stencil teardown calls this even after the parent text or renderer
        // is gone. Skip only the render submission; OnDisable must finish cleanup.
        if (__instance == null || !__instance.IsActive() || __instance.canvasRenderer == null)
            return false;
        var parent = __instance.textComponent;
        return parent != null && parent.fontSharedMaterial != null;
    }
}

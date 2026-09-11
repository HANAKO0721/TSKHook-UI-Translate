using HarmonyLib;
using UnityEngine.EventSystems;

namespace TSKHook.UI;

internal static class UguiButtonSeLifecyclePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(global::Utage.UguiButtonSe), "OnPointerClick")]
    internal static bool BeforeClick(global::Utage.UguiButtonSe __instance, PointerEventData __0)
    {
        if (__instance.gameObject.activeInHierarchy) return true;
        // Button.onClick may already have closed this object before its sound handler runs.
        // Keep the native pointer filter and sound, without starting the flag-reset coroutine.
        if (__0.pointerId is -1 or 0)
            __instance.PlayeSe(__instance.clickedPlayMode, __instance.clicked, __instance.clicked.name);
        return false;
    }
}

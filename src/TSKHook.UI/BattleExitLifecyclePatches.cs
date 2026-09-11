using HarmonyLib;

namespace TSKHook.UI;

internal static class BattleExitLifecyclePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(global::TSKBattleManager.__c__DisplayClass44_0), "_End_b__0")]
    internal static void BeforeFadeOutComplete(global::TSKBattleManager.__c__DisplayClass44_0 __instance)
    {
        // The native callback forgets async scene unloading, then releases battle assets.
        // ResultCanvas and CharacterCanvas share BattleRoot; the manager has a separate root.
        __instance.__4__this.resultRoot.transform.root.gameObject.SetActive(false);
    }
}

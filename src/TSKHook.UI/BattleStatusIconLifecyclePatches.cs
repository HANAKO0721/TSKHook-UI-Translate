using HarmonyLib;

namespace TSKHook.UI;

internal static class BattleStatusIconLifecyclePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(global::TSKBattleBuffDebuffIcon.__c__DisplayClass9_0), "_SetImageIcon_b__0")]
    internal static bool BeforeSpriteLoaded(global::TSKBattleBuffDebuffIcon.__c__DisplayClass9_0 __instance)
    {
        // DataReSet destroys status icons while their sprite loads can still finish.
        // The loader owns the asset; only this expired view callback must be skipped.
        var owner = __instance.__4__this;
        return owner != null && owner.iconImage != null;
    }
}

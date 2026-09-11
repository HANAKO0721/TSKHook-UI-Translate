using HarmonyLib;
using TKS.Header.Presenter;
using TKS.Network.Domain;

namespace TSKHook.UI;

internal static class MockBattleResultPatches
{
    private static TSKBattleResultUnitParamData? headerRank;

    [HarmonyPostfix, HarmonyPatch(typeof(HeaderPresenter), "InjectData",
        new[] { typeof(UserEntity), typeof(bool), typeof(TextType), typeof(bool), typeof(bool) })]
    internal static void CaptureHeader(UserEntity __0)
    {
        // Keep the same display values that the native header just received.
        headerRank = new TSKBattleResultUnitParamData {
            lv = __0.user_rank, exp = __0.start_total_exp,
            exp_max = __0.next_rank_exp, isMax = __0.max_rank_flg != 0
        };
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TSKBattleResultTribeGathering), "Initialize",
        new[] { typeof(BattleResultType), typeof(BattleEndRepository), typeof(BattleStartResultEntity), typeof(int), typeof(int) })]
    internal static void AfterInitialize(TSKBattleResultTribeGathering __instance)
    {
        if (!__instance.IsMockBattle || __instance.IsWin || headerRank is not { } rank) return;
        // Mock defeats have no rank reward payload. Show the unchanged current
        // rank instead of the synthetic zero values, without changing player data.
        __instance.playerRankBefore = rank;
        __instance.playerRankAfter = rank;
        __instance.playerRankLvUp.Initialize(rank, rank, true);
    }
}

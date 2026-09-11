using HarmonyLib;
using TKS.Network.Domain;

namespace TSKHook.UI;

internal static class GiftHistoryIconPatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(PopupPresentBoxSubListItemModel),
        nameof(PopupPresentBoxSubListItemModel.InjectData),
        new[] { typeof(PresentBoxHistoryItemEntity) })]
    internal static void AfterHistoryInjected(PopupPresentBoxSubListItemModel __instance,
        PresentBoxHistoryItemEntity __0)
    {
        // This history entry supplies a friend-point illustration for Tels.
        // Correct only the display model; retain the original reward entity.
        if (__0.present_name == "テルス" && __0.present_illust_id == "item_0003001")
            __instance.SetItemIllustId("item_0002001");
    }
}

using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TKS.Network.Domain;

namespace TSKHook.UI;

// Observe catalogue payloads at their model entry points. Item and product getters
// can share native implementations, so they must not be used as patch targets.
internal static class CatalogCapturePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(WarehouseTopModel), nameof(WarehouseTopModel.InjectData),
        new[] { typeof(Il2CppReferenceArray<WarehouseListEntity>) })]
    private static void WarehouseItems(Il2CppReferenceArray<WarehouseListEntity> __0)
    {
        if (__0 == null) return;
        foreach (var list in __0)
        {
            if (list == null) continue;
            CaptureWarehouseItems(list.item_list, "Items");
            CaptureWarehouseItems(list.stren_list, "Materials");
            CaptureWarehouseItems(list.chara_piece_list, "CharacterPieces");
            CaptureWarehouseItems(list.event_item_list, "EventItems");
        }
    }

    private static void CaptureWarehouseItems(Il2CppReferenceArray<WarehouseItemEntity>? items, string category)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null) continue;
            var prefix = $"Warehouse/{category}/{item.item_id}";
            Plugin.Translator.CaptureSource(item.item_name, $"{prefix}/item_name");
            Plugin.Translator.CaptureSource(item.detail, $"{prefix}/detail");
            Plugin.Translator.CaptureSource(item.limit_date_text, $"{prefix}/limit_date_text");
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ShopListModel), nameof(ShopListModel.InjectData),
        new[] { typeof(Il2CppReferenceArray<ShopEntity>), typeof(Il2CppReferenceArray<CostEntity>),
            typeof(ShopType), typeof(SubscriptionTextDataEntity) })]
    private static void ShopCatalogue(Il2CppReferenceArray<ShopEntity> __0,
        Il2CppReferenceArray<CostEntity> __1, SubscriptionTextDataEntity __3)
    {
        if (__0 != null)
            foreach (var shop in __0)
                if (shop != null) CaptureShop(shop);

        if (__1 != null)
        {
            foreach (var cost in __1)
            {
                if (cost == null) continue;
                var prefix = $"Shop/Cost/{cost.item_id}";
                Plugin.Translator.CaptureSource(cost.item_name, $"{prefix}/item_name");
                Plugin.Translator.CaptureSource(cost.detail, $"{prefix}/detail");
            }
        }

        if (__3 != null)
        {
            Plugin.Translator.CaptureSource(__3.popup_text, "Shop/Subscription/popup_text");
            Plugin.Translator.CaptureSource(__3.attention_text, "Shop/Subscription/attention_text");
        }
    }

    private static void CaptureShop(ShopEntity shop)
    {
        var prefix = $"Shop/{shop.shop_id}";
        Plugin.Translator.CaptureSource(shop.shop_name, $"{prefix}/shop_name");
        Plugin.Translator.CaptureSource(shop.shop_end_text, $"{prefix}/shop_end_text");
        Plugin.Translator.CaptureSource(shop.banner_text, $"{prefix}/banner_text");

        var tabs = shop.sub_tab_list;
        if (tabs != null)
            foreach (var tab in tabs)
                if (tab != null)
                    Plugin.Translator.CaptureSource(tab.tab_name, $"{prefix}/SubTab/tab_name");

        var products = shop.product_list;
        if (products == null) return;
        foreach (var product in products)
        {
            if (product == null) continue;
            var productPrefix = $"{prefix}/Product/{product.product_id}";
            Plugin.Translator.CaptureSource(product.product_name, $"{productPrefix}/product_name");
            Plugin.Translator.CaptureSource(product.product_detail, $"{productPrefix}/product_detail");
            Plugin.Translator.CaptureSource(product.stock_reset_text, $"{productPrefix}/stock_reset_text");
            Plugin.Translator.CaptureSource(product.payment_detail, $"{productPrefix}/payment_detail");
            Plugin.Translator.CaptureSource(product.unlock_condition_text, $"{productPrefix}/unlock_condition_text");
            Plugin.Translator.CaptureSource(product.tab_name, $"{productPrefix}/tab_name");
            CaptureProductDetails(product.product_detail_list, $"{productPrefix}/Contents");
            CaptureProductDetails(product.home_reward_list, $"{productPrefix}/HomeRewards");
        }
    }

    private static void CaptureProductDetails(Il2CppReferenceArray<ProductDetailEntity>? rewards, string prefix)
    {
        if (rewards == null) return;
        foreach (var reward in rewards)
        {
            if (reward == null) continue;
            var rewardPrefix = $"{prefix}/{reward.reward_id}";
            Plugin.Translator.CaptureSource(reward.reward_name, $"{rewardPrefix}/reward_name");
            Plugin.Translator.CaptureSource(reward.detail, $"{rewardPrefix}/detail");
            Plugin.Translator.CaptureSource(reward.limit_date_text, $"{rewardPrefix}/limit_date_text");
        }
    }
}

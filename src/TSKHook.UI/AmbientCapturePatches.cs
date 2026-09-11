using HarmonyLib;

namespace TSKHook.UI;

// Read the complete balloon list after the owning screen finishes normal setup.
internal static class AmbientCapturePatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(QuestRootModel), nameof(QuestRootModel.InjectData), new Type[] { })]
    private static void QuestBalloons(QuestRootModel __instance)
        => CaptureBalloons(__instance.Chara, "Quest");

    [HarmonyPostfix, HarmonyPatch(typeof(StoryRootModel), nameof(StoryRootModel.InjectData), new Type[] { })]
    private static void StoryBalloons(StoryRootModel __instance)
        => CaptureBalloons(__instance.Chara, "Story");

    private static void CaptureBalloons(IPictureModel? picture, string scope)
    {
        var texts = picture?.GetBalloonTextsValue();
        var count = texts?.Count ?? 0;
        Plugin.Info($"Captured {scope} balloon list: {count} source strings");
        if (texts == null) return;
        for (var index = 0; index < count; index++)
            Plugin.Translator.CaptureSource(texts[index], $"Ambient/{scope}/Balloon/{index}");
    }
}

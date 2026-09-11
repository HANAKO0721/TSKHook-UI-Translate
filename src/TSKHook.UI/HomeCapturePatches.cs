using HarmonyLib;
using TKS.Network.Domain;

namespace TSKHook.UI;

// Capture the report's complete voice-text list when normal home data arrives.
internal static class HomeCapturePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(HomeModel), nameof(HomeModel.InjectData),
        new[] { typeof(HomeDataRepository) })]
    private static void HomeReport(HomeDataRepository __0)
    {
        var report = __0?.result?.report_info;
        var voices = report?.voice_list;
        Plugin.Info($"Captured home report voice list: {voices?.Length ?? 0} source strings");
        if (voices == null) return;
        foreach (var voice in voices)
        {
            if (voice == null) continue;
            var location = $"Home/Report/{report!.home_report_id}/Voice/{voice.voice_id}/text";
            Plugin.Translator.CaptureSource(voice.text, location);
        }
    }
}

using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TKS.Network.Domain;

namespace TSKHook.UI;

// The normal story-list responses include unread entries before their rows are
// rendered. Capture that payload without opening episodes or changing unlocks.
internal static class StoryTitleCapturePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(MainStoryListModel), nameof(MainStoryListModel.SetMainStoryList),
        new[] { typeof(MainStoryPartListEntity), typeof(int), typeof(bool) })]
    private static void MainStories(MainStoryPartListEntity __0)
    {
        if (__0?.part_list == null) return;
        foreach (var part in __0.part_list)
        {
            if (part == null) continue;
            var prefix = $"StoryTitles/Main/Part/{part.part_number}";
            Capture(part.unlock_condition_text, prefix + "/unlock_condition_text");
            if (part.chapter_list == null) continue;
            foreach (var chapter in part.chapter_list)
            {
                if (chapter == null) continue;
                var path = $"{prefix}/Chapter/{chapter.chapter_no}";
                Capture(chapter.chapter_name, path + "/chapter_name");
                Capture(chapter.unlock_condition_text, path + "/unlock_condition_text");
                CaptureScenarios(chapter.scenario_list, path);
            }
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(EventStoryListModel), nameof(EventStoryListModel.SetEventStoryList),
        new[] { typeof(EventStoryChapterListEntity) })]
    private static void EventStories(EventStoryChapterListEntity __0)
    {
        if (__0?.event_list == null) return;
        foreach (var entry in __0.event_list)
        {
            if (entry == null) continue;
            var path = $"StoryTitles/Event/{entry.quest_id}";
            Capture(entry.quest_name, path + "/quest_name");
            CaptureScenarios(entry.scenario_list, path);
        }
    }

    private static void CaptureScenarios(Il2CppReferenceArray<ScenarioDetailEntity>? scenarios, string prefix)
    {
        if (scenarios == null) return;
        foreach (var scenario in scenarios)
        {
            if (scenario == null) continue;
            // adv_file_name identifies the upstream translation chapter; the
            // label identifies the episode in that file. Keep both for reuse.
            var path = $"{prefix}/Scenario/{scenario.adv_id}/{scenario.adv_file_name}/{scenario.adv_label_name}";
            Capture(scenario.scenario_name, path + "/scenario_name");
            Capture(scenario.unlock_condition_text, path + "/unlock_condition_text");
            Capture(scenario.caution_text, path + "/caution_text");
        }
    }

    private static void Capture(string source, string path)
        => Plugin.Translator.CaptureSource(source, path);
}
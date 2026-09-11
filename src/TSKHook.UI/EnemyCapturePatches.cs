using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TKS.Network.Domain;

namespace TSKHook.UI;

// Called by the verified stage-view and tower-repository capture boundaries.
// This helper registers no native detours and only reads normally loaded text.
internal static class EnemyCapture
{
    internal static void CaptureStage(QuestStageDetailResultEntity? stage)
    {
        if (stage == null) return;
        var scope = $"EnemyStage/{stage.stage_id}";
        var enemies = stage.enemy_list;
        if (enemies != null)
            foreach (var enemy in enemies) CaptureEnemy(enemy, scope);
        CaptureWeakData(stage.weak_gauge_data_list, scope);
    }
    internal static void CaptureEnemy(QuestStageEnemyEntity? enemy, string scope)
    {
        if (enemy == null) return;
        var prefix = $"{scope}/Enemy/{enemy.unit_illust_id}";
        var translator = Plugin.Translator;
        translator.CaptureSource(enemy.character_name, $"{prefix}/character_name");
        translator.CaptureSource(enemy.unit_name, $"{prefix}/unit_name");
        translator.CaptureSource(enemy.unit_detail, $"{prefix}/unit_detail");
        var skills = enemy.skill_data;
        if (skills != null)
        {
            foreach (var skill in skills)
            {
                if (skill == null) continue;
                var skillPrefix = $"{prefix}/Skill/{skill.skill_id}";
                translator.CaptureSource(skill.skill_name, $"{skillPrefix}/skill_name");
                translator.CaptureSource(skill.skill_detail, $"{skillPrefix}/skill_detail");
                translator.CaptureSource(skill.unlock_condition, $"{skillPrefix}/unlock_condition");
                translator.CaptureSource(skill.specific_skill_name, $"{skillPrefix}/specific_skill_name");
                translator.CaptureSource(skill.specific_skill_detail, $"{skillPrefix}/specific_skill_detail");
                translator.CaptureSource(skill.enemy_special_skill_trigger_text, $"{skillPrefix}/special_trigger");
                translator.CaptureSource(skill.enemy_weak_gauge_decrease_condition_text, $"{skillPrefix}/weak_gauge_condition");
            }
        }
        CaptureWeakData(enemy.weak_gauge_data_list, prefix);
    }

    private static void CaptureWeakData(Il2CppReferenceArray<BattleStartWeakGaugeData>? rows, string scope)
    {
        if (rows == null) return;
        foreach (var row in rows)
            if (row != null)
                Plugin.Translator.CaptureSource(row.detail, $"{scope}/WeakGauge/{row.weak_gauge_id}/detail");
    }
}


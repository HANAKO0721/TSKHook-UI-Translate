using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TKS.Network.Domain;

namespace TSKHook.UI;

// Capture game-content text while the owning model receives its normal data.
// These hooks never write to the entities or alter the game's return values.
internal static class DataCapturePatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(SisterListDataModel), nameof(SisterListDataModel.InjectData),
        new[] { typeof(Il2CppReferenceArray<SisterEntity>) })]
    private static void SisterListData(Il2CppReferenceArray<SisterEntity> __0)
    {
        if (__0 == null) return;
        foreach (var sister in __0) CaptureSisterData(sister);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(SisterModel), nameof(SisterModel.InjectData),
        new[] { typeof(SisterEntity) })]
    private static void SisterEntityData(SisterEntity __0) => CaptureSisterData(__0);

    [HarmonyPrefix, HarmonyPatch(typeof(SisterModel), nameof(SisterModel.InjectData),
        new[] { typeof(SisterExpedtionSisterDataEntity) })]
    private static void SisterExpeditionData(SisterExpedtionSisterDataEntity __0)
    {
        if (__0 == null) return;
        CaptureSisterData(__0);
        var prefix = $"SisterSkill.Name/{__0.sister_unit_id}/Expedition";
        Plugin.Translator.CaptureSource(__0.active_skill_name, $"{prefix}/active_skill_name");
        Plugin.Translator.CaptureSource(__0.support_skill_name, $"{prefix}/support_skill_name");
    }

    private static void CaptureSisterData(SisterEntity entity)
    {
        if (entity == null) return;
        var id = entity.sister_unit_id;
        Plugin.Translator.CaptureSource(entity.character_name, $"Sister/{id}/character_name");
        Plugin.Translator.CaptureSource(entity.sister_name, $"Sister/{id}/sister_name");
        Plugin.Translator.CaptureSource(entity.support_skill_detail_display,
            $"SisterSkill.Detail/{id}/SupportDisplay");

        var active = entity.active_skill_data;
        if (active != null)
        {
            var prefix = $"{id}/Active/{active.active_skill_id}";
            CaptureSisterSkill(active.skill_name, active.skill_detail, $"{prefix}/lv/{active.lv}");
            var levels = active.next_lv_list;
            if (levels != null)
                foreach (var level in levels)
                    if (level != null)
                        CaptureSisterSkill(level.skill_name, level.skill_detail, $"{prefix}/lv/{level.lv}");
        }

        var support = entity.support_skill_data;
        if (support != null)
        {
            var prefix = $"{id}/Support/{support.support_skill_id}";
            CaptureSisterSkill(support.skill_name, support.skill_detail, $"{prefix}/lv/{support.lv}");
            var levels = support.next_lv_list;
            if (levels != null)
                foreach (var level in levels)
                    if (level != null)
                        CaptureSisterSkill(level.skill_name, level.skill_detail, $"{prefix}/lv/{level.lv}");
        }

        var extra = entity.extra_support_skill_data;
        var release = extra?.release_skill_data;
        if (release != null)
            CaptureSisterSkill(release.skill_name, release.skill_detail,
                $"{id}/ExSupport/{extra!.extra_support_skill_id}");
    }

    private static void CaptureSisterSkill(string name, string detail, string location)
    {
        Plugin.Translator.CaptureSource(name, $"SisterSkill.Name/{location}");
        Plugin.Translator.CaptureSource(detail, $"SisterSkill.Detail/{location}");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(EquipTopModel), nameof(EquipTopModel.InjectEquipPartData),
        new[] { typeof(EquipListRepository) })]
    private static void EquipmentInventory(EquipListRepository __0)
    {
        var parts = __0?.result?.equip_part_list;
        if (parts == null) return;
        foreach (var part in parts)
        {
            var equipment = part?.equip_list;
            if (equipment == null) continue;
            foreach (var item in equipment) CaptureEquipment(item);
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(EquipDataModel), nameof(EquipDataModel.InjectData),
        new[] { typeof(EquipListEntity) })]
    private static void EquipmentData(EquipListEntity __0) => CaptureEquipment(__0);

    [HarmonyPrefix, HarmonyPatch(typeof(EquipPartDataModel), nameof(EquipPartDataModel.InjectData),
        new[] { typeof(PictureBookEquipListEntity) })]
    private static void EquipmentPictureBook(PictureBookEquipListEntity __0)
    {
        var equipment = __0?.equip_list;
        if (equipment == null) return;
        foreach (var item in equipment)
        {
            if (item == null) continue;
            var prefix = $"Equip/{item.equip_id}";
            Plugin.Translator.CaptureSource(item.equip_name, $"{prefix}/equip_name");
            Plugin.Translator.CaptureSource(item.exclusive_unit_name, $"{prefix}/exclusive_unit_name");
            var phases = item.phase_status_list;
            if (phases == null) continue;
            foreach (var phase in phases)
            {
                if (phase != null)
                    CaptureEquipmentSkill(phase.skill_data, $"{prefix}/phase/{phase.phase}");
            }
        }
    }

    private static void CaptureEquipment(EquipListEntity entity)
    {
        if (entity == null) return;
        var prefix = $"Equip/{entity.equip_id}";
        Plugin.Translator.CaptureSource(entity.equip_name, $"{prefix}/equip_name");
        Plugin.Translator.CaptureSource(entity.exclusive_unit_name, $"{prefix}/exclusive_unit_name");
        CaptureEquipmentSkill(entity.skill_data, prefix);
        var frames = entity.enchant_frame_list;
        if (frames == null) return;
        foreach (var frame in frames)
        {
            var effect = frame?.enchant_data;
            if (effect == null) continue;
            Plugin.Translator.CaptureSource(effect.detail,
                $"{prefix}/enchant/{frame!.frame_no}/skill/{effect.skill_id}/detail");
        }
    }

    private static void CaptureEquipmentSkill(EquipSkillDataEntity? skill, string prefix)
    {
        if (skill == null) return;
        Plugin.Translator.CaptureSource(skill.skill_detail,
            $"{prefix}/skill/{skill.skill_id}/lv/{skill.lv}/skill_detail");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(SkillModel), nameof(SkillModel.SetSpecificName),
        new[] { typeof(string) })]
    private static void SpecificSkillName(string __0)
        => Plugin.Translator.CaptureSource(__0, "SkillModel.Name/Specific");

    [HarmonyPrefix, HarmonyPatch(typeof(SkillModel), nameof(SkillModel.SetSpecificDetail),
        new[] { typeof(string) })]
    private static void SpecificSkillDetail(string __0)
        => Plugin.Translator.CaptureSource(__0, "SkillModel.Detail/Specific");

    [HarmonyPrefix, HarmonyPatch(typeof(SkillModel), nameof(SkillModel.SetUnlockConditionValue),
        new[] { typeof(string) })]
    private static void SkillUnlockCondition(string __0)
        => Plugin.Translator.CaptureSource(__0, "SkillModel.Detail/UnlockCondition");

    [HarmonyPrefix, HarmonyPatch(typeof(SkillModel), nameof(SkillModel.SetSpCondition),
        new[] { typeof(string) })]
    private static void SpecialSkillCondition(string __0)
        => Plugin.Translator.CaptureSource(__0, "SkillModel.Detail/SpCondition");

    [HarmonyPrefix, HarmonyPatch(typeof(SkillModel), nameof(SkillModel.SetWeakGaugeDecreaseCondition),
        new[] { typeof(string) })]
    private static void SkillWeakGaugeCondition(string __0)
        => Plugin.Translator.CaptureSource(__0, "SkillModel.Detail/WeakGaugeDecreaseCondition");

    // These models' low-level setters recurse through IL2CPP detours in this build.
    // Capture the displayed skill at its view boundary instead.
    [HarmonyPrefix, HarmonyPatch(typeof(SisterDetailStatusSkillView), nameof(SisterDetailStatusSkillView.UpdateView),
        new[] { typeof(int), typeof(int), typeof(string), typeof(string), typeof(int), typeof(bool) })]
    private static void SisterSkillView(string __2, string __3)
    {
        Plugin.Translator.CaptureSource(__2, "SisterSkill.Name");
        Plugin.Translator.CaptureSource(__3, "SisterSkill.Detail");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(MissionContentModel), nameof(MissionContentModel.InjectData),
        new[] { typeof(MissionDataEntity), typeof(string) })]
    private static void Mission(MissionDataEntity __0) => CaptureMission(__0);

    [HarmonyPrefix, HarmonyPatch(typeof(SpecialMissionContentModel), nameof(SpecialMissionContentModel.InjectData),
        new[] { typeof(SpecialMissionDataEntity) })]
    private static void SpecialMission(SpecialMissionDataEntity __0)
    {
        if (__0 == null) return;
        CaptureMission(__0);
        Plugin.Translator.CaptureSource(__0.mission_info_text, $"Mission/{__0.mission_id}/mission_info_text");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(NomalMissionModel), nameof(NomalMissionModel.InjectData),
        new[] { typeof(MissionEntity) })]
    private static void MissionTab(MissionEntity __0)
    {
        if (__0 == null) return;
        var prefix = $"MissionGroup/{__0.mission_group_id}";
        Plugin.Translator.CaptureSource(__0.tab_name, $"{prefix}/tab_name");
        Plugin.Translator.CaptureSource(__0.unlock_condition_text, $"{prefix}/unlock_condition_text");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(MissionGroupModel), nameof(MissionGroupModel.InjectData),
        new[] { typeof(MissionGroupListEntity) })]
    private static void MissionGroup(MissionGroupListEntity __0)
    {
        if (__0 == null) return;
        var prefix = $"MissionGroup/{__0.mission_group_id}";
        Plugin.Translator.CaptureSource(__0.special_mission_name, $"{prefix}/special_mission_name");
        Plugin.Translator.CaptureSource(__0.limit_date_text, $"{prefix}/limit_date_text");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(UnitModel), nameof(UnitModel.InjectData),
        new[] { typeof(UnitEntity) })]
    private static void Unit(UnitEntity __0) => CaptureUnit(__0);

    [HarmonyPrefix, HarmonyPatch(typeof(UnitModel), nameof(UnitModel.InjectData),
        new[] { typeof(UnitEntity), typeof(bool) })]
    private static void TransformedUnit(UnitEntity __0) => CaptureUnit(__0);

    [HarmonyPrefix, HarmonyPatch(typeof(UnitModel), nameof(UnitModel.InjectProfileData),
        new[] { typeof(PictureBookUnitEntity) })]
    private static void UnitProfile(PictureBookUnitEntity __0)
    {
        if (__0 == null) return;
        var prefix = $"Unit/{__0.unit_id}";
        var translator = Plugin.Translator;
        translator.CaptureSource(__0.character_name, $"{prefix}/character_name");
        translator.CaptureSource(__0.full_name, $"{prefix}/full_name");
        translator.CaptureSource(__0.unit_name, $"{prefix}/unit_name");
        translator.CaptureSource(__0.profile, $"{prefix}/profile");
        translator.CaptureSource(__0.birthday, $"{prefix}/birthday");
        translator.CaptureSource(__0.guardian_star, $"{prefix}/guardian_star");
        translator.CaptureSource(__0.committee, $"{prefix}/committee");
        translator.CaptureSource(__0.club, $"{prefix}/club");
        translator.CaptureSource(__0.hobby, $"{prefix}/hobby");
        translator.CaptureSource(__0.cv, $"{prefix}/cv");
        translator.CaptureSource(__0.awake_profile, $"{prefix}/awake_profile");
        translator.CaptureSource(__0.awake_character_name, $"{prefix}/awake_character_name");
        translator.CaptureSource(__0.awake_full_name, $"{prefix}/awake_full_name");
        translator.CaptureSource(__0.awake_unit_name, $"{prefix}/awake_unit_name");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(QuestListModel), nameof(QuestListModel.InjectData),
        new[] { typeof(QuestStageListRepository), typeof(int) })]
    private static void MainQuests(QuestStageListRepository __0)
    {
        var contents = __0?.result?.contents_list;
        if (contents == null) return;
        foreach (var content in contents)
        {
            if (content == null) continue;
            Plugin.Translator.CaptureSource(content.unlock_condition,
                $"QuestList/{content.class_type}/unlock_condition");
            var chapters = content.quest_list;
            if (chapters == null) continue;
            foreach (var chapter in chapters)
            {
                if (chapter == null) continue;
                var prefix = $"Quest/{chapter.quest_id}";
                Plugin.Translator.CaptureSource(chapter.quest_name, $"{prefix}/quest_name");
                Plugin.Translator.CaptureSource(chapter.unlock_condition, $"{prefix}/unlock_condition");
                Plugin.Translator.CaptureSource(chapter.challenge_popup_text, $"{prefix}/challenge_popup_text");
                CaptureStages(chapter.stage_list);
            }
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(QuestListModel), nameof(QuestListModel.InjectData),
        new[] { typeof(SearchQuestListRepository), typeof(int) })]
    private static void SearchQuests(SearchQuestListRepository __0)
    {
        var contents = __0?.result?.contents_list;
        if (contents == null) return;
        foreach (var content in contents)
        {
            if (content == null) continue;
            var contentPrefix = $"SearchQuest/{content.quest_type}";
            Plugin.Translator.CaptureSource(content.unlock_condition, $"{contentPrefix}/unlock_condition");
            Plugin.Translator.CaptureSource(content.daily_quest_all_skip_status_text,
                $"{contentPrefix}/daily_quest_all_skip_status_text");
            var quests = content.quest_list;
            if (quests == null) continue;
            foreach (var quest in quests)
            {
                if (quest == null) continue;
                var prefix = $"Quest/{quest.quest_id}";
                Plugin.Translator.CaptureSource(quest.quest_name, $"{prefix}/quest_name");
                Plugin.Translator.CaptureSource(quest.unlock_condition, $"{prefix}/unlock_condition");
                CaptureStages(quest.stage_list);
            }
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(FinisQuestListModel), nameof(FinisQuestListModel.InjectData),
        new[] { typeof(FinisQuestStageListRepository), typeof(eFinisGateType), typeof(int) })]
    private static void TowerStages(FinisQuestStageListRepository __0)
    {
        var result = __0?.result;
        if (result == null) return;
        var stages = result.stage_list;
        if (stages != null)
        {
            foreach (var stage in stages)
            {
                if (stage == null) continue;
                // stage_name is an integer floor label, not a source text field.
                var scope = $"FinisStage/{stage.stage_id}/Floor/{stage.stage_no}";
                var enemies = stage.enemy_list;
                if (enemies != null)
                    foreach (var enemy in enemies) EnemyCapture.CaptureEnemy(enemy, scope);

                var fieldWaves = stage.stage_field_effect_list;
                if (fieldWaves != null)
                {
                    foreach (var wave in fieldWaves)
                    {
                        var effects = wave?.field_effect_list;
                        if (effects == null) continue;
                        foreach (var effect in effects)
                        {
                            if (effect == null) continue;
                            var prefix = $"{scope}/Field/{wave!.wave}/{effect.field_effect_id}";
                            Plugin.Translator.CaptureSource(effect.field_effect_name, $"{prefix}/field_effect_name");
                            Plugin.Translator.CaptureSource(effect.detail, $"{prefix}/detail");
                            Plugin.Translator.CaptureSource(effect.condition_text, $"{prefix}/condition_text");
                        }
                    }
                }

                var stageRewards = stage.stage_reward_list;
                if (stageRewards == null) continue;
                foreach (var reward in stageRewards)
                {
                    if (reward == null) continue;
                    var prefix = $"{scope}/Reward/{reward.reward_type}/{reward.reward_id}";
                    Plugin.Translator.CaptureSource(reward.reward_name, $"{prefix}/reward_name");
                    Plugin.Translator.CaptureSource(reward.detail, $"{prefix}/detail");
                }
            }
        }

        var rewardGroups = result.reward_list;
        if (rewardGroups == null) return;
        foreach (var group in rewardGroups)
        {
            var rewards = group?.achieved_list;
            if (rewards == null) continue;
            foreach (var reward in rewards)
            {
                if (reward == null) continue;
                var prefix = $"FinisReward/{group!.achieved_reward_type}/Achieved/{reward.achieved_id}/{reward.reward_id}";
                Plugin.Translator.CaptureSource(reward.reward_name, $"{prefix}/reward_name");
                Plugin.Translator.CaptureSource(reward.detail, $"{prefix}/detail");
            }
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TeamTopView), nameof(TeamTopView.UpdateStageData),
        new[] { typeof(int), typeof(int), typeof(bool), typeof(QuestStageDetailResultEntity) })]
    private static void StageDetail(QuestStageDetailResultEntity __3)
    {
        if (__3 == null) return;
        EnemyCapture.CaptureStage(__3);
        var prefix = $"Stage/{__3.stage_id}";
        Plugin.Translator.CaptureSource(__3.stage_name, $"{prefix}/stage_name");
        Plugin.Translator.CaptureSource(__3.lock_skip_popup_text, $"{prefix}/lock_skip_popup_text");
        var conditions = __3.stage_rank_condition_list;
        if (conditions == null) return;
        foreach (var condition in conditions)
        {
            if (condition == null) continue;
            Plugin.Translator.CaptureSource(condition.detail,
                $"{prefix}/rank/{condition.rank}/{condition.condition_type}/detail");
        }
    }

    private static void CaptureMission(MissionDataEntity data)
    {
        if (data == null) return;
        var prefix = $"Mission/{data.mission_id}";
        var translator = Plugin.Translator;
        translator.CaptureSource(data.mission_name, $"{prefix}/mission_name");
        translator.CaptureSource(data.mission_short_name, $"{prefix}/mission_short_name");
        translator.CaptureSource(data.limit_date_text, $"{prefix}/limit_date_text");
        translator.CaptureSource(data.contents_unlock_condition_text, $"{prefix}/contents_unlock_condition_text");
    }

    private static void CaptureUnit(UnitEntity entity)
    {
        if (entity == null) return;
        var prefix = $"Unit/{entity.unit_id}";
        var translator = Plugin.Translator;
        translator.CaptureSource(entity.character_name, $"{prefix}/character_name");
        translator.CaptureSource(entity.full_name, $"{prefix}/full_name");
        translator.CaptureSource(entity.unit_name, $"{prefix}/unit_name");
        translator.CaptureSource(entity.profile, $"{prefix}/profile");
        translator.CaptureSource(entity.birthday, $"{prefix}/birthday");
        translator.CaptureSource(entity.guardian_star, $"{prefix}/guardian_star");
        translator.CaptureSource(entity.committee, $"{prefix}/committee");
        translator.CaptureSource(entity.club, $"{prefix}/club");
        translator.CaptureSource(entity.hobby, $"{prefix}/hobby");
        translator.CaptureSource(entity.cv, $"{prefix}/cv");
    }

    private static void CaptureStages(Il2CppReferenceArray<StageListEntity>? stages)
    {
        if (stages == null) return;
        foreach (var stage in stages)
        {
            if (stage == null) continue;
            var prefix = $"Stage/{stage.stage_id}";
            Plugin.Translator.CaptureSource(stage.stage_name, $"{prefix}/stage_name");
            Plugin.Translator.CaptureSource(stage.unlock_condition, $"{prefix}/unlock_condition");
            Plugin.Translator.CaptureSource(stage.popup_text, $"{prefix}/popup_text");
        }
    }
}

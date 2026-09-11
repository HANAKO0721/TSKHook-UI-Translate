using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TSKHook.UI;

[BepInPlugin("TSKHook.UI", "TSKHook UI", "0.3.4")]
[BepInDependency("TSKHook")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    internal static UiTranslation Translator = null!;
    internal static UiBehaviour Behaviour = null!;

    public override void Load()
    {
        Instance = this;
        UnityExceptionCapture.Start();
        if (ConsoleInputMode.DisableQuickEdit())
            Info("已停用本次遊戲主控台的快速編輯，避免選取日誌暫停遊戲。");
        // Preserve the native call stack for the Unity exceptions seen during UI teardown.
        Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
        Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
        var workspace = new WorkspaceSync(Path.Combine(Paths.PluginPath, "TSKHook.UI"), Info);
        Info("Shared UI/API configuration: " + workspace.ConfigurationFile());
        Translator = new UiTranslation(workspace);
        Translator.Reload();
        Harmony.CreateAndPatchAll(typeof(TmpSubMeshLifecyclePatches), "TSKHook.UI.TmpLifecycle");
        Harmony.CreateAndPatchAll(typeof(RaycastMaskLifecyclePatches), "TSKHook.UI.RaycastMaskLifecycle");
        Harmony.CreateAndPatchAll(typeof(SkeletonGraphicLifecyclePatches), "TSKHook.UI.SkeletonGraphicLifecycle");
        Harmony.CreateAndPatchAll(typeof(UguiButtonSeLifecyclePatches), "TSKHook.UI.UguiButtonSeLifecycle");
        Harmony.CreateAndPatchAll(typeof(BattleExitLifecyclePatches), "TSKHook.UI.BattleExitLifecycle");
        Harmony.CreateAndPatchAll(typeof(BattleStatusIconLifecyclePatches), "TSKHook.UI.BattleStatusIconLifecycle");
        Harmony.CreateAndPatchAll(typeof(MockBattleResultPatches), "TSKHook.UI.MockBattleResult");
        Harmony.CreateAndPatchAll(typeof(GiftHistoryIconPatches), "TSKHook.UI.GiftHistoryIcon");
        Harmony.CreateAndPatchAll(typeof(TextPatches), "TSKHook.UI");
        Harmony.CreateAndPatchAll(typeof(DataCapturePatches), "TSKHook.UI.Capture");
        Harmony.CreateAndPatchAll(typeof(CatalogCapturePatches), "TSKHook.UI.CatalogCapture");
        Harmony.CreateAndPatchAll(typeof(HomeCapturePatches), "TSKHook.UI.HomeCapture");
        Harmony.CreateAndPatchAll(typeof(AmbientCapturePatches), "TSKHook.UI.AmbientCapture");
        Harmony.CreateAndPatchAll(typeof(StoryTitleCapturePatches), "TSKHook.UI.StoryTitles");
        Behaviour = AddComponent<UiBehaviour>();
        Harmony.CreateAndPatchAll(typeof(SpriteViewLoadPatches), "TSKHook.UI.SpriteViewLoad");
        Log.LogInfo("UI translator loaded. F9: reload translations; F4: capture loaded UI text.");
    }

    internal static void Info(string message) => Instance.Log.LogInfo(message);
    internal static void Error(Exception error) => Instance.Log.LogError(error);
    internal static void Error(string message) => Instance.Log.LogError(message);
}

public sealed class UiBehaviour : MonoBehaviour
{
    private float nextFlush;
    private bool initialized;
    private bool wasEnabled;

    public void OnDestroy() => Plugin.Translator?.Dispose();

    public void Update()
    {
        if (!initialized)
        {
            initialized = true;
            wasEnabled = global::TSKHook.TSKConfig.TranslationEnabled;
            try
            {
                Plugin.Translator.EnsureFont();
                Plugin.Translator.RefreshLoadedText();
            }
            catch (Exception error) { Plugin.Error(error); }
        }
        Plugin.Translator.ProcessQueuedUi();
        Plugin.Translator.ProcessOnlineTranslations();
        WebTranslations.Tick(global::TSKHook.TSKConfig.TranslationEnabled);
        if (wasEnabled != global::TSKHook.TSKConfig.TranslationEnabled)
        {
            wasEnabled = global::TSKHook.TSKConfig.TranslationEnabled;
            Plugin.Translator.RefreshLoadedText();
        }
        if (Input.GetKeyDown(KeyCode.F9))
        {
            try
            {
                Plugin.Translator.Reload();
                Plugin.Translator.RefreshLoadedText();
            }
            catch (Exception error) { Plugin.Error(error); }
        }
        if (Input.GetKeyDown(KeyCode.F4))
        {
            try { Plugin.Translator.CaptureLoadedText(); }
            catch (Exception error) { Plugin.Error(error); }
        }
        if (Time.unscaledTime >= nextFlush)
        {
            nextFlush = Time.unscaledTime + 3;
            try
            {
                Plugin.Translator.RefreshChangedTranslations();
                Plugin.Translator.Flush();
            }
            catch (Exception error) { Plugin.Error(error); }
        }
    }
}

internal static class TextPatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(Spine.Unity.SkeletonGraphic), "UpdateMesh")]
    private static void BeforeSpineMesh(Spine.Unity.SkeletonGraphic __instance)
        => SpineTitleMasks.BeforeMesh(__instance);

    [HarmonyFinalizer, HarmonyPatch(typeof(Spine.Unity.SkeletonGraphic), "UpdateMesh")]
    private static Exception? DiagnoseSpineMesh(Spine.Unity.SkeletonGraphic __instance, Exception? __exception)
    {
        if (__exception != null) SpineMeshDiagnostics.Capture(__instance);
        return __exception;
    }
    [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.UI.Image), "set_sprite")]
    private static void SetSprite(UnityEngine.UI.Image __instance) => Plugin.Translator.QueueSprite(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.UI.Image), "set_overrideSprite")]
    private static void SetOverrideSprite(UnityEngine.UI.Image __instance) => Plugin.Translator.QueueSprite(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.UI.RawImage), "set_texture")]
    private static void SetRawTexture(UnityEngine.UI.RawImage __instance) => Plugin.Translator.QueueRawImage(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.UI.Graphic), "OnEnable")]
    private static void EnableGraphic(UnityEngine.UI.Graphic __instance)
    {
        var image = __instance.TryCast<UnityEngine.UI.Image>();
        if (image != null) Plugin.Translator.QueueSprite(image);
        var rawImage = __instance.TryCast<UnityEngine.UI.RawImage>();
        if (rawImage != null) Plugin.Translator.QueueRawImage(rawImage);
        var spine = __instance.TryCast<Spine.Unity.SkeletonGraphic>();
        if (spine != null) Plugin.Translator.QueueSpineLabel(spine);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.UI.Graphic), "OnRectTransformDimensionsChange")]
    private static void ResizeGraphic(UnityEngine.UI.Graphic __instance)
    {
        var image = __instance.TryCast<UnityEngine.UI.Image>();
        if (image != null) Plugin.Translator.QueueSprite(image);
        var spine = __instance.TryCast<Spine.Unity.SkeletonGraphic>();
        if (spine != null) Plugin.Translator.QueueSpineLabel(spine);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.UI.Graphic), "OnDisable")]
    private static void DisableGraphic(UnityEngine.UI.Graphic __instance)
    {
        var image = __instance.TryCast<UnityEngine.UI.Image>();
        if (image != null) Plugin.Translator.RefreshSprite(image, false);
        var rawImage = __instance.TryCast<UnityEngine.UI.RawImage>();
        if (rawImage != null) Plugin.Translator.RefreshRawImage(rawImage, false);
        var spine = __instance.TryCast<Spine.Unity.SkeletonGraphic>();
        if (spine != null) Plugin.Translator.RefreshSpineLabel(spine, false);
    }


    [HarmonyPrefix, HarmonyPatch(typeof(UnitModel), "SetNameValue")]
    private static void CaptureUnitName(string __0) => Plugin.Translator.CaptureSource(__0, "UnitModel.Name");

    [HarmonyPrefix, HarmonyPatch(typeof(UnitModel), "SetNickNameValue")]
    private static void CaptureUnitTitle(string __0) => Plugin.Translator.CaptureSource(__0, "UnitModel.Title");

    [HarmonyPrefix, HarmonyPatch(typeof(SkillModel), "SetNameValue")]
    private static void CaptureSkillName(string __0) => Plugin.Translator.CaptureSource(__0, "SkillModel.Name");

    [HarmonyPrefix, HarmonyPatch(typeof(SkillModel), "SetDetailValue")]
    private static void CaptureSkillDetail(string __0) => Plugin.Translator.CaptureSource(__0, "SkillModel.Detail");

    // Observe every TMP input mode without changing an in-progress render pass.
    [HarmonyPrefix, HarmonyPatch(typeof(TMP_Text), "ParseInputText")]
    private static void ParseInputText(TMP_Text __instance)
        => Plugin.Translator.QueueText(__instance);

    [HarmonyPrefix, HarmonyPatch(typeof(TextMeshProUGUI), "OnDestroy")]
    private static void DestroyUiText(TextMeshProUGUI __instance) => Plugin.Translator.ReleaseText(__instance);

    [HarmonyPrefix, HarmonyPatch(typeof(TextMeshPro), "OnDestroy")]
    private static void DestroyWorldText(TextMeshPro __instance) => Plugin.Translator.ReleaseText(__instance);

    [HarmonyPrefix, HarmonyPatch(typeof(UnityEngine.UI.Text), "set_text")]
    private static void SetText(UnityEngine.UI.Text __instance, ref string value)
        => value = Plugin.Translator.Translate(__instance, value);

    [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.UI.Text), "OnEnable")]
    private static void EnableText(UnityEngine.UI.Text __instance)
    {
        var original = __instance.text;
        var translated = Plugin.Translator.Translate(__instance, original);
        if (translated != original) __instance.text = translated;
    }
}

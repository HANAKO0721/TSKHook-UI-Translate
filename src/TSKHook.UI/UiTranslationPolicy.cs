namespace TSKHook.UI;

// These are observed game hierarchies: dialogue belongs to TSKHook, and
// editable fields / friend names belong to the player.
internal static class UiTranslationPolicy
{
    internal static bool PreserveOriginal(string path, bool insideInput)
        => insideInput
            || path.EndsWith("/TeamTopView(Clone)/Canvas/WindowRoot/TeamRoot/TeamNameArea/ChangeRoot/TeamName/TeamNameText", StringComparison.Ordinal)
            || (path.Contains("/PopupPresetTeamListView(Clone)/Window/TabView/TabRoot/", StringComparison.Ordinal)
                && path.EndsWith("/TKSTextTMPGUI", StringComparison.Ordinal))
            || (path.Contains("/PopupPresetTeamListView(Clone)/Window/ListRoot/PresetListView/Viewport/Content/", StringComparison.Ordinal)
                && path.EndsWith("/PopupListItemView/ChangeRoot/TeamName/TeamNameText", StringComparison.Ordinal))
            || path.EndsWith("/ProfileTopView(Clone)/Canvas/WindowRoot/Background/Profile/StatusWindow/ContentRoot/Name/param", StringComparison.Ordinal)
            || path.EndsWith("/ProfileTopView(Clone)/Canvas/WindowRoot/Background/Profile/CommentWindow/Comment/comment", StringComparison.Ordinal)
            || path.EndsWith("/PopupProfile(Clone)/Window/Body/WindowRoot (1)/Background/Profile/StatusWindow/ContentRoot/Name/param", StringComparison.Ordinal)
            || path.EndsWith("/PopupProfile(Clone)/Window/Body/WindowRoot (1)/Background/Profile/CommentWindow/Comment/comment", StringComparison.Ordinal)
            || path.EndsWith("/TrophyTopView(Clone)/Canvas/CharaRoot/CharaNameRoot/Name", StringComparison.Ordinal)
            || (path.Contains("/AdvRoot/", StringComparison.Ordinal)
                && (path.Contains("/MessageWindow", StringComparison.Ordinal)
                    || path.Contains("/GraphicManager/Title/", StringComparison.Ordinal)
                    || path.Contains("/UI/Selection/", StringComparison.Ordinal)))
            || (path.Contains("/FriendTopView(Clone)/UICanvas/Background/FriendListView/", StringComparison.Ordinal)
                && (path.EndsWith("/bg/UserName", StringComparison.Ordinal)
                    || path.EndsWith("/bg/Comment_bg/comment", StringComparison.Ordinal)));
}

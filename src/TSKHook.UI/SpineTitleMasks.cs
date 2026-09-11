using Il2CppInterop.Runtime;
using Spine;
using Spine.Unity;

namespace TSKHook.UI;

// The source masks trace Japanese lettering. Keep translated sweep masks on
// individual slots so the shared SkeletonData and its original skins stay intact.
internal static class SpineTitleMasks
{
    private static readonly Dictionary<int, MaskState> states = new();

    internal static void Track(SkeletonGraphic graphic, float[] vertices)
    {
        var asset = graphic.skeletonDataAsset;
        if (!graphic.IsValid || asset == null) return;
        var id = graphic.GetInstanceID();
        if (states.ContainsKey(id))
        {
            BeforeMesh(graphic);
            return;
        }
        var skeleton = graphic.Skeleton;
        var slot = skeleton.FindSlot("title_mask") ?? skeleton.FindSlot("Title_mask");
        var original = slot?.Attachment?.TryCast<ClippingAttachment>();
        if (original == null) return;
        var replacement = original.Copy().Cast<ClippingAttachment>();
        var scale = asset.scale;
        replacement.Vertices = vertices.Select(value => value * scale).ToArray();
        replacement.WorldVerticesLength = vertices.Length;
        states[id] = new MaskState(asset, skeleton, slot!,
            original, replacement, vertices);
        slot!.Attachment = replacement;
    }

    // UpdateMesh runs after animation application and also during UI Rebuild.
    // A setup-pose reset may restore the original attachment between these calls.
    internal static void BeforeMesh(SkeletonGraphic graphic)
    {
        if (!states.TryGetValue(graphic.GetInstanceID(), out var state)) return;
        if (!graphic.enabled || graphic.skeletonDataAsset != state.Asset)
        {
            Restore(graphic);
            return;
        }
        if (!graphic.IsValid)
        {
            // Clear can precede Initialize on the same live graphic. Restore the
            // old slot, retaining the contour needed when its skeleton returns.
            Restore(state);
            return;
        }
        if (graphic.Skeleton.Pointer != state.Skeleton.Pointer)
        {
            Restore(graphic);
            Track(graphic, state.Vertices);
            return;
        }
        var current = state.Slot.Attachment;
        if (current != null && current.Pointer == state.Original.Pointer)
            state.Slot.Attachment = state.Replacement;
    }

    internal static void Restore(SkeletonGraphic graphic)
    {
        if (!states.Remove(graphic.GetInstanceID(), out var state)) return;
        Restore(state);
    }

    internal static void Clear()
    {
        foreach (var state in states.Values) Restore(state);
        states.Clear();
    }

    internal static object? CaptureState(SkeletonGraphic graphic)
    {
        if (!states.TryGetValue(graphic.GetInstanceID(), out var state)) return null;
        var current = state.Slot.Attachment?.TryCast<ClippingAttachment>();
        return new { translated = current != null && current.Pointer == state.Replacement.Pointer,
            appliedVertices = current?.Vertices.ToArray(),
            originalVertices = state.Original.Vertices.ToArray() };
    }

    private static void Restore(MaskState state)
    {
        var current = state.Slot.Attachment;
        if (current != null && current.Pointer == state.Replacement.Pointer)
            state.Slot.Attachment = state.Original;
    }

    private sealed record MaskState(UnityEngine.Object Asset, Skeleton Skeleton, Slot Slot,
        ClippingAttachment Original, ClippingAttachment Replacement, float[] Vertices);
}

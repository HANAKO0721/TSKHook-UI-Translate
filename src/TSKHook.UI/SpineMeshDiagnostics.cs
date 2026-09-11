using System.Text;
using Spine.Unity;

namespace TSKHook.UI;

// Capture only a failed native mesh pass. The caller preserves its original exception.
internal static class SpineMeshDiagnostics
{
    internal static void Capture(SkeletonGraphic graphic)
    {
        var report = new StringBuilder("Spine mesh failure diagnostic:\n");
        try
        {
            report.AppendLine($"graphic: rawNull={ReferenceEquals(graphic, null)}, unityAlive={graphic != null}");
            if (graphic != null)
            {
                var names = new List<string>();
                for (var current = graphic.transform; current != null; current = current.parent)
                    names.Add(current.name);
                names.Reverse();
                report.AppendLine($"path={string.Join("/", names)}");
                report.AppendLine($"active={graphic.gameObject.activeInHierarchy}, enabled={graphic.enabled}, isValid={graphic.IsValid}, multipleRenderers={graphic.allowMultipleCanvasRenderers}");
                var asset = graphic.skeletonDataAsset;
                report.AppendLine($"asset: rawNull={ReferenceEquals(asset, null)}, unityAlive={asset != null}, name={(asset != null ? asset.name : "<unavailable>")}");
                var separators = graphic.separatorSlots;
                report.AppendLine($"separatorSlots: count={separators?.Count ?? 0}");
                if (separators != null)
                    for (var i = 0; i < separators.Count; i++)
                    {
                        var slot = separators[i];
                        report.AppendLine($"  [{i}] name={slot?.Data?.Name ?? "<null>"}");
                    }
                var instructions = graphic.currentInstructions?.submeshInstructions;
                report.AppendLine($"submeshInstructions: count={instructions?.Count ?? 0}");
                if (instructions != null)
                    for (var i = 0; i < instructions.Count; i++)
                    {
                        var instruction = instructions.Items[i];
                        var material = instruction.material;
                        var alive = material != null;
                        report.AppendLine($"  [{i}] rawVertexCount={instruction.rawVertexCount}, slots={instruction.startSlot}..{instruction.endSlot}, material.rawNull={ReferenceEquals(material, null)}, material.unityAlive={alive}, material.name={(alive ? material!.name : "<unavailable>")}");
                    }
            }
        }
        catch (Exception diagnosticError)
        {
            report.AppendLine($"Diagnostic read failed: {diagnosticError.GetType().Name}: {diagnosticError.Message}");
        }
        // A failure of this diagnostic sink must not replace the native mesh exception.
        try { Plugin.Error(report.ToString()); }
        catch (Exception) { }
    }
}

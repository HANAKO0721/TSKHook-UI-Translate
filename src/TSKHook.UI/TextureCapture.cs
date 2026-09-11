using UnityEngine;

namespace TSKHook.UI;

// F4 exports the help texture currently displayed by the game, without changing its asset.
internal static class TextureCapture
{
    internal static void Save(Texture source, WorkspaceSync workspace, string? assetName = null)
    {
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(source.width, source.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D? readable = null;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply(false, false);
            var name = string.Concat((assetName ?? source.name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            workspace.WriteCapture(Path.Combine("textures", name + ".png"),
                ImageConversion.EncodeToPNG(readable).ToArray());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
    }
}

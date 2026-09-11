namespace TSKHook.UI;

/// <summary>Shared pixel coordinates for the API canvas and Unity's source-size cache restoration.</summary>
internal sealed record OnlineImageCanvas(int CanvasWidth, int CanvasHeight, int Left, int Top,
    int ContentWidth, int ContentHeight)
{
    internal static OnlineImageCanvas ForSource(int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        var canvasWidth = width > height ? 1536 : 1024;
        var canvasHeight = height > width ? 1536 : 1024;
        if (width == canvasWidth && height == canvasHeight)
            return new OnlineImageCanvas(width, height, 0, 0, width, height);
        var scale = Math.Min((canvasWidth - 64d) / width, (canvasHeight - 64d) / height);
        if (scale >= 1) scale = Math.Floor(scale);
        var contentWidth = Math.Max(1, (int)Math.Round(width * scale));
        var contentHeight = Math.Max(1, (int)Math.Round(height * scale));
        return new OnlineImageCanvas(canvasWidth, canvasHeight,
            (canvasWidth - contentWidth) / 2, (canvasHeight - contentHeight) / 2,
            contentWidth, contentHeight);
    }
}
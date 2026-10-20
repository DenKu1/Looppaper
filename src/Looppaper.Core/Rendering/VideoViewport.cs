namespace Looppaper.Core.Rendering;

/// <summary>Part of a video frame, in 0..1 coordinates of the frame.</summary>
public readonly record struct NormalizedRect(float Left, float Top, float Right, float Bottom)
{
    public static readonly NormalizedRect Full = new(0, 0, 1, 1);
}

public static class VideoViewport
{
    /// <summary>
    /// Splits a (possibly oversized) destination rectangle into the part of the video that is visible
    /// inside <paramref name="clip"/> and where that part lands. Lets the video renderer crop
    /// (fill / center) instead of drawing outside its window.
    /// </summary>
    public static (NormalizedRect Source, IntRect Destination) Visible(IntRect destination, IntRect clip)
    {
        var left = Math.Max(destination.X, clip.X);
        var top = Math.Max(destination.Y, clip.Y);
        var right = Math.Min(destination.Right, clip.Right);
        var bottom = Math.Min(destination.Bottom, clip.Bottom);
        if (destination.Width <= 0 || destination.Height <= 0 || right <= left || bottom <= top)
        {
            return (NormalizedRect.Full, new IntRect(clip.X, clip.Y, 0, 0));
        }

        var source = new NormalizedRect(
            (left - destination.X) / (float)destination.Width,
            (top - destination.Y) / (float)destination.Height,
            (right - destination.X) / (float)destination.Width,
            (bottom - destination.Y) / (float)destination.Height);
        return (source, new IntRect(left, top, right - left, bottom - top));
    }
}

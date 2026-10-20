using Looppaper.Core.Configuration;

namespace Looppaper.Core.Rendering;

public readonly record struct IntRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public override string ToString() => $"{Width}x{Height}@({X},{Y})";
}

public static class ScalingMath
{
    /// <summary>
    /// Where a picture of <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> is drawn for a
    /// monitor. The result may extend beyond the monitor (fill, center); callers clip to the monitor.
    /// </summary>
    public static IntRect Destination(int sourceWidth, int sourceHeight, IntRect monitor, ScalingMode mode)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || monitor.Width <= 0 || monitor.Height <= 0)
        {
            return new IntRect(monitor.X, monitor.Y, 0, 0);
        }

        if (mode == ScalingMode.Stretch)
        {
            return monitor;
        }

        var scaleX = monitor.Width / (double)sourceWidth;
        var scaleY = monitor.Height / (double)sourceHeight;
        var scale = mode switch
        {
            ScalingMode.Fill => Math.Max(scaleX, scaleY),
            ScalingMode.Fit => Math.Min(scaleX, scaleY),
            _ => 1.0,
        };

        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));

        // Snap to the monitor edge when rounding lands within a pixel, so no 1px background line shows.
        if (Math.Abs(width - monitor.Width) <= 1 && mode != ScalingMode.Center)
        {
            width = monitor.Width;
        }

        if (Math.Abs(height - monitor.Height) <= 1 && mode != ScalingMode.Center)
        {
            height = monitor.Height;
        }

        var x = monitor.X + ((monitor.Width - width) / 2);
        var y = monitor.Y + ((monitor.Height - height) / 2);
        return new IntRect(x, y, width, height);
    }
}

using System.Globalization;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Compression;

/// <summary>Width and height in pixels.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");
}

/// <summary>How an oversized video is re-encoded to fit the size limit.</summary>
public sealed record CompressionPlan(
    PixelSize Source,
    PixelSize Output,
    int VideoKbps,
    int AudioKbps,
    bool AudioDropped,
    TimeSpan Duration,
    long EstimatedBytes)
{
    public bool Scaled => Output != Source;

    public bool KeepsAudio => AudioKbps > 0;

    /// <summary>"about 92.0 MB (2560x1440, 11.4 Mbit/s video + 128 kbit/s audio, 1:05)".</summary>
    public string Describe()
    {
        var audio = KeepsAudio
            ? string.Create(CultureInfo.InvariantCulture, $" + {AudioKbps} kbit/s audio")
            : AudioDropped ? ", audio removed to fit" : string.Empty;
        var size = Scaled ? $"{Output} (scaled down from {Source})" : Output.ToString();
        var length = Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : Duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"about {VideoSizeLimit.FormatMb(EstimatedBytes)} ({size}, {VideoKbps / 1000.0:F1} Mbit/s video{audio}, {length})");
    }
}

/// <summary>
/// Plans the re-encode of an oversized video: a bitrate computed from the duration so the result lands
/// under the limit with a safety margin, a budget for audio (dropped only when the video would get too
/// little), and a scale-down to the screen when the video is larger (never up, aspect ratio kept).
/// </summary>
public static class CompressionPlanner
{
    /// <summary>Aim for this share of the limit; single-pass encoders overshoot a little and mp4 adds overhead.</summary>
    public const double DefaultMargin = 0.90;

    public const int AudioKbps = 128;

    /// <summary>Below this the video would look too poor; audio is dropped first, then the plan fails.</summary>
    public const int MinVideoKbps = 400;

    /// <summary>No point in going higher for a wallpaper.</summary>
    public const int MaxVideoKbps = 40_000;

    /// <summary>Container overhead included in the estimate (about 1%).</summary>
    public const double ContainerOverhead = 0.01;

    /// <summary>Returns the plan, or null with <paramref name="problem"/> set when the video cannot be made to fit.</summary>
    /// <param name="info">ffprobe output for the original.</param>
    /// <param name="limitBytes">Size limit of the result.</param>
    /// <param name="screen">Largest monitor; the output is scaled down to cover it. Null keeps the size.</param>
    /// <param name="margin">Share of the limit to aim for (lower for a retry).</param>
    public static CompressionPlan? Plan(MediaInfo info, long limitBytes, PixelSize? screen, double margin, out string? problem)
    {
        problem = null;
        if (info.Duration is not { TotalSeconds: > 0 } duration)
        {
            problem = "its length could not be read";
            return null;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            problem = "it has no readable video stream";
            return null;
        }

        var source = new PixelSize(info.Width, info.Height);
        var output = ScaleToCover(source, screen);

        var totalKbps = (int)Math.Floor(limitBytes * margin * 8 / 1000 / duration.TotalSeconds);
        var audioKbps = info.HasAudio ? AudioKbps : 0;
        var dropped = false;
        if (audioKbps > 0 && totalKbps - audioKbps < MinVideoKbps)
        {
            audioKbps = 0;
            dropped = true;
        }

        var videoKbps = Math.Min(totalKbps - audioKbps, MaxVideoKbps);
        if (videoKbps < MinVideoKbps)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"it is too long ({duration.TotalMinutes:F0} min) to fit in {VideoSizeLimit.FormatMb(limitBytes)} at a watchable quality");
            return null;
        }

        var estimated = (long)((videoKbps + audioKbps) * 1000 / 8.0 * duration.TotalSeconds * (1 + ContainerOverhead));
        return new CompressionPlan(source, output, videoKbps, audioKbps, dropped, duration, estimated);
    }

    public static CompressionPlan? Plan(MediaInfo info, long limitBytes, PixelSize? screen, out string? problem) =>
        Plan(info, limitBytes, screen, DefaultMargin, out problem);

    /// <summary>
    /// Margin for the one retry after a result of <paramref name="actualBytes"/> came out too large:
    /// scaled by how much it overshot, and at least 15% lower than before.
    /// </summary>
    public static double RetryMargin(double previousMargin, long actualBytes, long limitBytes)
    {
        var scaled = previousMargin * limitBytes / Math.Max(actualBytes, 1) * 0.93;
        return Math.Min(previousMargin * 0.85, scaled);
    }

    /// <summary>
    /// Scales <paramref name="source"/> down so it still covers <paramref name="screen"/> (the wallpaper
    /// fills the monitor), keeping the aspect ratio. Never scales up. Dimensions are made even for H.264.
    /// </summary>
    public static PixelSize ScaleToCover(PixelSize source, PixelSize? screen)
    {
        var factor = 1.0;
        if (screen is { Width: > 0, Height: > 0 } s)
        {
            factor = Math.Min(1.0, Math.Max(s.Width / (double)source.Width, s.Height / (double)source.Height));
        }

        var width = factor < 1.0 ? (int)Math.Round(source.Width * factor) : source.Width;
        var height = factor < 1.0 ? (int)Math.Round(source.Height * factor) : source.Height;
        return new PixelSize(Math.Max(2, width & ~1), Math.Max(2, height & ~1));
    }
}

using System.Globalization;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Compression;

/// <summary>Width and height in pixels.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");
}

/// <summary>How a video is re-encoded for the wallpaper.</summary>
/// <param name="Worthwhile">False when the result would not be noticeably smaller than the original (it is then kept as is).</param>
public sealed record CompressionPlan(
    PixelSize Source,
    PixelSize Output,
    int VideoKbps,
    int AudioKbps,
    TimeSpan Duration,
    long EstimatedBytes,
    bool Worthwhile)
{
    public bool Scaled => Output != Source;

    public bool KeepsAudio => AudioKbps > 0;

    /// <summary>"about 42.0 MB (2560x1440, 11.1 Mbit/s video + 128 kbit/s audio, 1:05)".</summary>
    public string Describe()
    {
        var audio = KeepsAudio ? string.Create(CultureInfo.InvariantCulture, $" + {AudioKbps} kbit/s audio") : string.Empty;
        var size = Scaled ? $"{Output} (scaled down from {Source})" : Output.ToString();
        var length = Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : Duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"about {VideoSize.FormatMb(EstimatedBytes)} ({size}, {VideoKbps / 1000.0:F1} Mbit/s video{audio}, {length})");
    }
}

/// <summary>
/// Plans the re-encode of a wallpaper video: scale down to the screen when the video is larger (never up,
/// aspect ratio kept) and use a bitrate that looks good for a wallpaper at that size and frame rate,
/// never more than the original's. Any length works; there is no size target.
/// </summary>
public static class CompressionPlanner
{
    /// <summary>Video bits per pixel per frame: plenty for H.264 at wallpaper viewing (~11 Mbit/s at 1440p30).</summary>
    public const double BitsPerPixel = 0.10;

    public const int AudioKbps = 128;

    public const int MinVideoKbps = 400;

    public const int MaxVideoKbps = 40_000;

    /// <summary>Container overhead included in the estimate (about 1%).</summary>
    public const double ContainerOverhead = 0.01;

    /// <summary>Compressing is worth it only if the copy is at least this much smaller (or scaled down).</summary>
    public const double MinimumSaving = 0.15;

    /// <summary>Returns the plan, or null with <paramref name="problem"/> set when the video cannot be read.</summary>
    /// <param name="info">ffprobe output for the original.</param>
    /// <param name="screen">Largest monitor; the output is scaled down to cover it. Null keeps the size.</param>
    public static CompressionPlan? Plan(MediaInfo info, PixelSize? screen, out string? problem)
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
        var fps = Math.Clamp(info.FrameRate ?? 30, 1, 60);
        var audioKbps = info.HasAudio ? AudioKbps : 0;

        var qualityKbps = (int)(BitsPerPixel * output.Width * output.Height * fps / 1000);
        var videoKbps = qualityKbps;
        if (info.BitRate is { } total && total > 0)
        {
            // Never spend more bits than the original had (minus its audio).
            var sourceVideoKbps = (int)(total / 1000) - audioKbps;
            videoKbps = Math.Min(videoKbps, Math.Max(sourceVideoKbps, MinVideoKbps));
        }

        videoKbps = Math.Clamp(videoKbps, MinVideoKbps, MaxVideoKbps);
        var estimated = (long)((videoKbps + audioKbps) * 1000 / 8.0 * duration.TotalSeconds * (1 + ContainerOverhead));
        var originalBytes = info.SizeBytes ?? long.MaxValue;
        var worthwhile = output != source || estimated < originalBytes * (1 - MinimumSaving);
        return new CompressionPlan(source, output, videoKbps, audioKbps, duration, estimated, worthwhile);
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

using System.Globalization;
using Looppaper.Core.Logging;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Compression;

/// <summary>An ffmpeg H.264 encoder and the options Looppaper uses with it.</summary>
public sealed record VideoEncoder(string Name, string Label, bool IsHardware, string PixelFormat, IReadOnlyList<string> Options)
{
    public static readonly VideoEncoder Nvenc = new("h264_nvenc", "NVIDIA NVENC", true, "yuv420p", ["-preset", "p5", "-tune", "hq", "-rc", "vbr"]);
    public static readonly VideoEncoder Amf = new("h264_amf", "AMD AMF", true, "yuv420p", ["-quality", "quality", "-rc", "vbr_peak"]);
    public static readonly VideoEncoder Qsv = new("h264_qsv", "Intel Quick Sync", true, "nv12", ["-preset", "slow"]);
    public static readonly VideoEncoder X264 = new("libx264", "CPU (x264)", false, "yuv420p", ["-preset", "medium"]);

    /// <summary>Hardware encoders first (fast, little CPU), then the CPU.</summary>
    public static readonly IReadOnlyList<VideoEncoder> PreferenceOrder = [Nvenc, Amf, Qsv, X264];

    public override string ToString() => $"{Label} ({Name})";
}

/// <summary>Builds ffmpeg argument lists (passed as a list, never as one string).</summary>
public static class CompressionArguments
{
    /// <summary>Re-encodes <paramref name="input"/> to an H.264 / AAC mp4 at <paramref name="output"/> as planned.</summary>
    public static IReadOnlyList<string> Encode(string input, string output, CompressionPlan plan, VideoEncoder encoder)
    {
        var v = plan.VideoKbps;
        var args = new List<string> { "-y", "-i", input, "-map", "0:v:0" };
        if (plan.KeepsAudio)
        {
            args.AddRange(["-map", "0:a:0?"]);
        }

        args.AddRange(["-map_metadata", "-1", "-sn", "-dn"]);
        if (plan.Scaled)
        {
            args.AddRange(["-vf", Invariant($"scale={plan.Output.Width}:{plan.Output.Height}:flags=lanczos")]);
        }

        args.AddRange(["-c:v", encoder.Name]);
        args.AddRange(encoder.Options);
        args.AddRange(["-b:v", Invariant($"{v}k"), "-maxrate", Invariant($"{v * 3 / 2}k"), "-bufsize", Invariant($"{v * 2}k"), "-pix_fmt", encoder.PixelFormat]);
        if (plan.KeepsAudio)
        {
            args.AddRange(["-c:a", "aac", "-b:a", Invariant($"{plan.AudioKbps}k"), "-ac", "2"]);
        }
        else
        {
            args.Add("-an");
        }

        args.AddRange(["-movflags", "+faststart", "-f", "mp4", output]);
        return args;
    }

    /// <summary>A tiny test encode of a generated black clip, to see whether an encoder works on this PC.</summary>
    public static IReadOnlyList<string> ProbeEncoder(VideoEncoder encoder)
    {
        var args = new List<string> { "-f", "lavfi", "-i", "color=c=black:s=320x240:r=30:d=1", "-frames:v", "5", "-c:v", encoder.Name };
        args.AddRange(encoder.Options);
        args.AddRange(["-b:v", "1000k", "-pix_fmt", encoder.PixelFormat, "-an", "-f", "null", "-"]);
        return args;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Picks the first encoder that works: NVENC, AMF, Quick Sync, then x264 on the CPU.</summary>
public static class EncoderSelector
{
    public static async Task<VideoEncoder> SelectAsync(IFfmpegExecutor ffmpeg, ILog log, CancellationToken cancellationToken)
    {
        foreach (var encoder in VideoEncoder.PreferenceOrder)
        {
            if (!encoder.IsHardware)
            {
                return encoder; // the CPU encoder is the fallback; a failure shows up in the real encode
            }

            var result = await ffmpeg.RunAsync(CompressionArguments.ProbeEncoder(encoder), null, null, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Succeeded)
            {
                log.Info($"Video encoder: {encoder}");
                return encoder;
            }

            log.Info($"Encoder {encoder.Name} is not available: {result.ErrorSummary}");
        }

        return VideoEncoder.X264;
    }
}

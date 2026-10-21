// Ported from wp2video (https://github.com/unio207/wp2video), MIT License — see THIRD-PARTY-NOTICES.md
using System.Globalization;
using System.Text.RegularExpressions;
using Looppaper.Core.Configuration;
using Looppaper.Core.Media;

namespace Looppaper.Core.WallpaperEngine;

/// <summary>Size of the Wallpaper Engine window to record and the part of it to keep.</summary>
/// <param name="Width">Output width.</param>
/// <param name="Height">Output height.</param>
/// <param name="RenderWidth">Window client width (wider than the output for a wider scene).</param>
/// <param name="RenderHeight">Window client height (taller than the output for a taller scene).</param>
/// <param name="Crop">gfxcapture crop options (<c>:crop_left=..:crop_right=..</c>), or empty.</param>
public sealed record CaptureGeometry(int Width, int Height, int RenderWidth, int RenderHeight, string Crop)
{
    /// <summary>
    /// For a scene with a different aspect ratio than the output, render the whole scene and crop it
    /// at the user's alignment position (0 = right / bottom, 100 = left / top, 50 = centered).
    /// </summary>
    public static CaptureGeometry For(int width, int height, (int Width, int Height)? sceneSize, double position)
    {
        width = Even(width);
        height = Even(height);
        if (sceneSize is not { } size)
        {
            return new CaptureGeometry(width, height, width, height, string.Empty);
        }

        var aspect = size.Width / (double)size.Height;
        if (aspect > width / (double)height + 0.01)
        {
            var renderWidth = (int)Math.Round(height * aspect / 2, MidpointRounding.ToEven) * 2;
            var left = (int)Math.Round((renderWidth - width) * (100 - position) / 100, MidpointRounding.ToEven);
            return new CaptureGeometry(width, height, renderWidth, height, Invariant($":crop_left={left}:crop_right={renderWidth - width - left}"));
        }

        if (aspect < width / (double)height - 0.01)
        {
            var renderHeight = (int)Math.Round(width / aspect / 2, MidpointRounding.ToEven) * 2;
            var top = (int)Math.Round((renderHeight - height) * (100 - position) / 100, MidpointRounding.ToEven);
            return new CaptureGeometry(width, height, width, renderHeight, Invariant($":crop_top={top}:crop_bottom={renderHeight - height - top}"));
        }

        return new CaptureGeometry(width, height, width, height, string.Empty);
    }

    private static int Even(int value) => Math.Max(2, value / 2 * 2);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>What will be recorded: length, whether the end is crossfaded into the start, and why.</summary>
public sealed record CapturePlan(double Seconds, bool Seamless, string Reason)
{
    /// <summary>Recording length when the wallpaper does not say how long it loops.</summary>
    public const double DefaultSeconds = 30;

    /// <summary>Loops longer than this are not recorded whole (the file would be too big or too soft).</summary>
    public const double MaxLoopToRecord = 120;

    /// <summary>Extra seconds recorded for the crossfade.</summary>
    public double FadeSeconds => Seamless ? SceneCapture.SeamlessFade(Seconds) : 0;

    public double RecordSeconds => Seconds + FadeSeconds;

    public static CapturePlan From(LoopLength? loop) =>
        loop is { Seconds: >= 1 and <= MaxLoopToRecord }
            ? new CapturePlan(loop.Seconds, false, string.Create(CultureInfo.InvariantCulture, $"loops every {loop.Seconds:0.###} s ({loop.Source})"))
            : new CapturePlan(DefaultSeconds, true, "it does not say how long it loops, so the end is crossfaded into the start");
}

/// <summary>
/// The ffmpeg command lines and decisions for recording a Wallpaper Engine window with ffmpeg 8+'s
/// <c>gfxcapture</c> source: encoder probing (NVENC / AMF / QSV, CPU fallback), frame rate,
/// recording, seamless crossfade and the final size limit. Pure functions; the caller runs ffmpeg.
/// </summary>
public static partial class SceneCapture
{
    /// <summary>The frame rate a converted wallpaper aims for (kept low: it plays all day).</summary>
    public const int TargetFps = 30;

    /// <summary>Converted files aim to stay under this, so they stay below the 100 MB size warning.</summary>
    public const long TargetBytes = 95 * MediaScanner.BytesPerMb;

    /// <summary>Above this a converted video is shrunk once more (and still plays, with a size warning, if it stays larger).</summary>
    public const long MaxBytes = Compression.VideoSize.WarningMb * MediaScanner.BytesPerMb;

    /// <summary>Bits per pixel per frame at most: the same quality target as video compression, so a converted scene is already compressed (no second encode).</summary>
    public const double MaxBitsPerPixel = Compression.CompressionPlanner.BitsPerPixel;

    private static readonly int[] StandardFps = [24, 30, 48, 60, 90, 120, 144, 165, 180, 240];

    /// <summary>Hardware encoders tried in order, before the CPU fallback.</summary>
    public static IReadOnlyList<string> Candidates(string codec) => [$"{codec}_nvenc", $"{codec}_amf", $"{codec}_qsv"];

    /// <summary>Arguments that encode 5 black frames with <paramref name="encoder"/>; exit code 0 means it works here.</summary>
    public static IReadOnlyList<string> ProbeEncoderArgs(string encoder) =>
        ["-v", "error", "-f", "lavfi", "-i", "color=c=black:s=1280x720:r=30", "-frames:v", "5", "-c:v", encoder, "-f", "null", "-"];

    /// <summary>The first working hardware encoder for the codec, else the CPU one.</summary>
    public static string PickEncoder(string codec, Func<string, bool> works)
    {
        foreach (var candidate in Candidates(codec))
        {
            if (works(candidate))
            {
                return candidate;
            }
        }

        return codec == "hevc" ? "libx265" : "libx264";
    }

    /// <summary>HEVC past H.264's practical limits (more than 4K at 60 fps), H.264 otherwise.</summary>
    public static string Codec(int width, int height, int fps) =>
        (long)width * height * fps > 3840L * 2160 * 60 ? "hevc" : "h264";

    /// <summary>
    /// The bitrate to record at: whatever keeps the file under <see cref="TargetBytes"/> for its
    /// length, but never more than <see cref="MaxBitsPerPixel"/>.
    /// </summary>
    public static long Bitrate(double seconds, int width, int height, int fps)
    {
        var budget = TargetBytes * 8 / Math.Max(seconds, 0.5);
        var quality = (double)width * height * fps * MaxBitsPerPixel;
        return Math.Max(500_000, (long)Math.Min(budget, quality));
    }

    /// <summary>Encoder options at a capped bitrate, so the file size is predictable.</summary>
    public static IReadOnlyList<string> EncoderArgs(string encoder, long bitsPerSecond)
    {
        var rate = bitsPerSecond.ToString(CultureInfo.InvariantCulture);
        var buffer = (bitsPerSecond * 2).ToString(CultureInfo.InvariantCulture);
        string[] limit = ["-b:v", rate, "-maxrate", rate, "-bufsize", buffer];
        if (encoder.EndsWith("_nvenc", StringComparison.Ordinal))
        {
            return ["-preset", "p4", "-tune", "hq", "-rc", "vbr", .. limit];
        }

        if (encoder.EndsWith("_amf", StringComparison.Ordinal))
        {
            return ["-quality", "quality", "-rc", "vbr_peak", .. limit];
        }

        if (encoder.EndsWith("_qsv", StringComparison.Ordinal))
        {
            return ["-preset", "medium", .. limit, "-pix_fmt", "nv12"];
        }

        return encoder == "libx265"
            ? ["-preset", "fast", .. limit, "-pix_fmt", "yuv420p"]
            : ["-preset", "veryfast", .. limit, "-pix_fmt", "yuv420p"];
    }

    /// <summary>Options for writing a final .mp4 from a file (always a yuv420p pixel format, HEVC tagged hvc1).</summary>
    public static IReadOnlyList<string> OutputArgs(string encoder, long bitsPerSecond)
    {
        var args = new List<string> { "-c:v", encoder };
        args.AddRange(EncoderArgs(encoder, bitsPerSecond));
        if (!args.Contains("-pix_fmt"))
        {
            args.AddRange(["-pix_fmt", "yuv420p"]);
        }

        args.AddRange(Mux(encoder));
        return args;
    }

    /// <summary>gfxcapture source for the window; NVENC takes the GPU frames directly, others need them downloaded.</summary>
    public static string CaptureSource(long hwnd, int fps, string crop, string encoder)
    {
        var source = string.Create(CultureInfo.InvariantCulture, $"gfxcapture=hwnd={hwnd}:max_framerate={fps}:capture_cursor=0{crop}");
        return encoder.EndsWith("_nvenc", StringComparison.Ordinal) ? source : source + ",hwdownload,format=bgra";
    }

    /// <summary>One tiny grey frame of the window on stdout, to see whether it draws anything yet.</summary>
    public static IReadOnlyList<string> ProbeDrawnArgs(long hwnd) =>
        ["-v", "error", "-filter_complex", string.Create(CultureInfo.InvariantCulture, $"gfxcapture=hwnd={hwnd}:capture_cursor=0,hwdownload,format=bgra,scale=32:18,format=gray"),
         "-frames:v", "1", "-f", "rawvideo", "-"];

    /// <summary>True once the grey thumbnail shows something other than black.</summary>
    public static bool IsDrawn(ReadOnlySpan<byte> grayPixels)
    {
        if (grayPixels.IsEmpty)
        {
            return false;
        }

        var max = 0;
        long sum = 0;
        foreach (var p in grayPixels)
        {
            max = Math.Max(max, p);
            sum += p;
        }

        return max > 12 || sum / (double)grayPixels.Length > 3;
    }

    /// <summary>Captures the window for 3 seconds without encoding, to count the frames it renders.</summary>
    public static IReadOnlyList<string> MeasureFpsArgs(long hwnd, int maxFps) =>
        ["-hide_banner", "-filter_complex", string.Create(CultureInfo.InvariantCulture, $"gfxcapture=hwnd={hwnd}:max_framerate={maxFps}:capture_cursor=0"),
         "-t", "3", "-fps_mode", "passthrough", "-f", "null", "-"];

    /// <summary>Frames per second from the last <c>frame=</c> count in ffmpeg's stderr after a 3 s capture.</summary>
    public static double ParseMeasuredFps(string stderr)
    {
        var matches = FrameCount().Matches(stderr);
        return matches.Count == 0 ? 0 : int.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture) / 3.0;
    }

    /// <summary>
    /// The recording frame rate: the target when the PC keeps up (gfxcapture delivers ~93% of a capped
    /// rate even then, so 90% counts), otherwise the highest standard rate it manages.
    /// </summary>
    public static int ChooseFps(double measured, int target = TargetFps)
    {
        var limit = measured >= target * 0.9 ? target : measured * 1.05;
        var fitting = StandardFps.Where(f => f <= limit).ToList();
        return fitting.Count == 0 ? 24 : fitting.Max();
    }

    /// <summary>Records the window to <paramref name="output"/> (a temporary .mkv).</summary>
    public static IReadOnlyList<string> RecordArgs(long hwnd, CaptureGeometry geometry, int fps, string encoder, long bitsPerSecond, double seconds, string output)
    {
        var args = new List<string>
        {
            "-y", "-filter_complex", CaptureSource(hwnd, fps, geometry.Crop, encoder),
            "-t", Seconds(seconds), "-fps_mode", "cfr", "-r", fps.ToString(CultureInfo.InvariantCulture),
            "-c:v", encoder,
        };
        args.AddRange(EncoderArgs(encoder, bitsPerSecond));
        args.Add(output);
        return args;
    }

    /// <summary>Crossfade length for a seamless loop: 1 s, or a quarter of a short clip.</summary>
    public static double SeamlessFade(double seconds) => Math.Min(1.0, seconds / 4);

    /// <summary>
    /// The source is <c>seconds + fade</c> long. The output starts <c>fade</c> seconds in, and its last
    /// <c>fade</c> seconds crossfade into the source's first <c>fade</c> seconds, so the last frame leads
    /// straight back into the first.
    /// </summary>
    public static IReadOnlyList<string> SeamlessArgs(string input, double seconds, string encoder, long bitsPerSecond, string output)
    {
        var x = SeamlessFade(seconds);
        var graph = string.Create(
            CultureInfo.InvariantCulture,
            $"[0:v]split[a][b];[a]trim={Seconds(x)}:{Seconds(seconds + x)},setpts=PTS-STARTPTS[a2];[b]trim=0:{Seconds(x)},setpts=PTS-STARTPTS[b2];[a2][b2]xfade=transition=fade:duration={Seconds(x)}:offset={Seconds(seconds - x)}[v]");
        var args = new List<string> { "-y", "-i", input, "-filter_complex", graph, "-map", "[v]" };
        args.AddRange(OutputArgs(encoder, bitsPerSecond));
        args.AddRange(["-an", output]);
        return args;
    }

    /// <summary>Copies the recording into an .mp4 without re-encoding.</summary>
    public static IReadOnlyList<string> RemuxArgs(string input, string encoder, string output)
    {
        var args = new List<string> { "-y", "-v", "error", "-i", input, "-c", "copy" };
        args.AddRange(Mux(encoder));
        args.AddRange(["-an", output]);
        return args;
    }

    /// <summary>Re-encodes a file that came out too large at a lower bitrate.</summary>
    public static IReadOnlyList<string> ShrinkArgs(string input, string encoder, long bitsPerSecond, string output)
    {
        var args = new List<string> { "-y", "-i", input };
        args.AddRange(OutputArgs(encoder, bitsPerSecond));
        args.AddRange(["-an", output]);
        return args;
    }

    /// <summary>Rough time the whole conversion takes: start-up, frame-rate test, recording and crossfade.</summary>
    public static TimeSpan Estimate(CapturePlan plan) =>
        TimeSpan.FromSeconds(15 + plan.RecordSeconds + (plan.Seamless ? plan.Seconds / 2 : 2));

    private static IReadOnlyList<string> Mux(string encoder) =>
        encoder.Contains("hevc", StringComparison.Ordinal) || encoder == "libx265"
            ? ["-tag:v", "hvc1", "-movflags", "+faststart"]
            : ["-movflags", "+faststart"];

    private static string Seconds(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"frame=\s*(\d+)")]
    private static partial Regex FrameCount();
}

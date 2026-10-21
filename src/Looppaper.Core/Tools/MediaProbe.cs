using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Looppaper.Core.Tools;

/// <summary>What ffprobe reports about a media file (the first video stream).</summary>
public sealed record MediaInfo(
    TimeSpan? Duration,
    int Width,
    int Height,
    string? VideoCodec,
    double? FrameRate,
    bool HasAudio,
    long? BitRate,
    long? SizeBytes);

public static class MediaProbe
{
    /// <summary>Runs ffprobe on <paramref name="file"/>; returns null when ffprobe is missing or fails.</summary>
    public static MediaInfo? Probe(FfmpegTool tool, string file, TimeSpan? timeout = null)
    {
        if (tool.FfprobePath is null)
        {
            return null;
        }

        var start = new ProcessStartInfo(tool.FfprobePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", file })
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var errorTask = process.StandardError.ReadToEndAsync();
            var json = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(timeout ?? TimeSpan.FromSeconds(30)))
            {
                process.Kill();
                return null;
            }

            _ = errorTask.Result;
            return process.ExitCode == 0 ? Parse(json) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Parses <c>ffprobe -print_format json -show_format -show_streams</c> output.</summary>
    public static MediaInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement? video = null;
            var hasAudio = false;
            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var type = String(stream, "codec_type");
                    if (type == "video" && video is null && !IsAttachedPicture(stream))
                    {
                        video = stream;
                    }
                    else if (type == "audio")
                    {
                        hasAudio = true;
                    }
                }
            }

            TimeSpan? duration = null;
            long? bitRate = null;
            long? size = null;
            if (root.TryGetProperty("format", out var format))
            {
                duration = Seconds(String(format, "duration"));
                bitRate = Long(String(format, "bit_rate"));
                size = Long(String(format, "size"));
            }

            if (video is not { } v)
            {
                return new MediaInfo(duration, 0, 0, null, null, hasAudio, bitRate, size);
            }

            duration ??= Seconds(String(v, "duration"));
            return new MediaInfo(
                duration,
                Int(v, "width"),
                Int(v, "height"),
                String(v, "codec_name"),
                Rate(String(v, "avg_frame_rate")) ?? Rate(String(v, "r_frame_rate")),
                hasAudio,
                bitRate,
                size);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var a) && a.ValueKind == JsonValueKind.Number && a.GetInt32() == 1;

    private static string? String(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) ? p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.GetRawText(),
            _ => null,
        } : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : 0;

    private static long? Long(string? s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;

    private static TimeSpan? Seconds(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? TimeSpan.FromSeconds(d) : null;

    /// <summary>"30000/1001" -> 29.97; "0/0" -> null.</summary>
    private static double? Rate(string? s)
    {
        if (s is null)
        {
            return null;
        }

        var parts = s.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && n > 0 && d > 0)
        {
            return n / d;
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0 ? r : null;
    }
}

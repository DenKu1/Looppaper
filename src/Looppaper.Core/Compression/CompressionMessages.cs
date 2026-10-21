namespace Looppaper.Core.Compression;

/// <summary>What Looppaper tells the person about compressing a video, in the terminal and in Windows notifications.</summary>
public static class CompressionMessages
{
    public const string Title = "Looppaper";

    /// <summary>Shown when compression starts.</summary>
    public static string Starting(VideoFileKey key, CompressionPlan plan, string cacheDirectory) =>
        $"Compressing {key.FileName} ({VideoSize.FormatMb(key.SizeBytes)}) for the wallpaper: {plan.Describe()}. "
        + $"The original is kept; the copy is saved in {cacheDirectory}.";

    /// <summary>Short notification text while the wallpaper compresses in the background.</summary>
    public static string StartingInBackground(VideoFileKey key) =>
        $"Compressing {key.FileName} ({VideoSize.FormatMb(key.SizeBytes)}) in the background. It plays as it is meanwhile; "
        + "the wallpaper switches to the lighter copy when it's ready. The original is kept.";

    /// <summary>The copy is still larger than <see cref="VideoSize.WarningMb"/>: it plays, but warn about performance.</summary>
    public static string LargeWarning(string fileName, long bytes) =>
        $"{fileName} is still {VideoSize.FormatMb(bytes)} after compression. Videos over {VideoSize.WarningMb} MB can slow your PC down "
        + "(more memory, disk and GPU use). It plays anyway; a shorter or lower-resolution video would be lighter.";

    /// <summary>The original is played as it is because ffmpeg is missing.</summary>
    public static string FfmpegMissing(VideoFileKey key, string installHint) =>
        $"Looppaper compresses wallpaper videos to keep them light, but ffmpeg was not found, so {key.FileName} "
        + $"({VideoSize.FormatMb(key.SizeBytes)}) plays uncompressed. {installHint}";
}

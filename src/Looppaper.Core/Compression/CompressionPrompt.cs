namespace Looppaper.Core.Compression;

/// <summary>The words used when offering to compress an oversized video, in the terminal and in the message box.</summary>
public static class CompressionPrompt
{
    public const string Title = "Looppaper - video too large";

    /// <summary>"Floating.mp4 is 218.3 MB, above the 100 MB video limit."</summary>
    public static string TooLarge(VideoFileKey key, int maxMb) =>
        $"{key.FileName} is {VideoSizeLimit.FormatMb(key.SizeBytes)}, above the {maxMb} MB video limit.";

    /// <summary>The lines shown before the question.</summary>
    public static string Details(VideoFileKey key, int maxMb, CompressionPlan plan, string cacheDirectory) =>
        TooLarge(key, maxMb) + Environment.NewLine
        + $"Estimated result: {plan.Describe()}." + Environment.NewLine
        + $"The original is kept; the copy is saved in {cacheDirectory}.";

    public static string ConsoleQuestion(int maxMb) => $"Compress to fit under {maxMb} MB? [y/N] ";

    public static string MessageBoxText(VideoFileKey key, int maxMb, CompressionPlan plan, string cacheDirectory) =>
        $"The wallpaper video {key.FullPath} is too large to play." + Environment.NewLine + Environment.NewLine
        + Details(key, maxMb, plan, cacheDirectory) + Environment.NewLine + Environment.NewLine
        + $"Compress it to fit under {maxMb} MB? This can take a few minutes; pictures are shown meanwhile. "
        + "If you choose No, Looppaper will not ask again for this file (run \"Looppaper.exe --compress <file>\" to compress it later).";

    public static string FfmpegMissingText(VideoFileKey key, int maxMb, string installHint) =>
        $"The wallpaper video {key.FullPath} is too large to play." + Environment.NewLine + Environment.NewLine
        + TooLarge(key, maxMb) + " Looppaper can compress it to fit, but needs ffmpeg for that." + Environment.NewLine + Environment.NewLine
        + installHint + Environment.NewLine + Environment.NewLine
        + "Pictures are shown instead. After installing ffmpeg, restart Looppaper or run \"Looppaper.exe --compress <file>\".";

    /// <summary>True for "y" or "yes" (any case); everything else, including no answer, means no.</summary>
    public static bool IsYes(string? answer) =>
        answer?.Trim().ToLowerInvariant() is "y" or "yes";
}

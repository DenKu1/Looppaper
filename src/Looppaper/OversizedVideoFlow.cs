using Looppaper.Core.Compression;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;

namespace Looppaper;

/// <summary>Who can answer when Looppaper offers to compress an oversized video.</summary>
internal enum OversizedVideoMode
{
    /// <summary>A person at a terminal: print the details and prompt there.</summary>
    Console,

    /// <summary>No console (the background wallpaper process): ask with a Windows message box.</summary>
    Background,
}

/// <summary>
/// What happens to a video above the size limit (<c>video.maxFileSizeMb</c>, at most 100 MB): it is
/// not played; Looppaper logs it and the caller falls back to pictures.
/// </summary>
internal static class OversizedVideoFlow
{
    /// <summary>
    /// Returns a path that can be played for <paramref name="videoPath"/>: the file itself when it is
    /// within the size limit, otherwise null (the caller shows pictures). Call it from any code that
    /// picked a video, e.g. a Wallpaper Engine item.
    /// </summary>
    /// <param name="videoPath">Full path of the chosen video.</param>
    /// <param name="config">Loaded config (size limit, ffmpeg path).</param>
    /// <param name="log">Log for what was decided.</param>
    /// <param name="mode">Whether a person at a terminal or a message box is asked.</param>
    public static string? Resolve(string videoPath, LooppaperConfig config, ILog log, OversizedVideoMode mode)
    {
        var key = VideoFileKey.TryFromFile(videoPath);
        if (key is null)
        {
            log.Warn($"Video {videoPath} does not exist");
            return null;
        }

        var choice = VideoChooser.Choose([key], config.Video.MaxFileSizeMb, k => FindCompressed(k, config, log), log);
        return choice.PlayPath;
    }

    /// <summary>The compressed copy of an oversized video, or null when there is none.</summary>
    public static string? FindCompressed(VideoFileKey key, LooppaperConfig config, ILog log) => null;
}

using Looppaper.Core.Logging;

namespace Looppaper.Core.Compression;

/// <summary>The video to play now, and the video to compress in the background (if any).</summary>
/// <param name="PlayPath">The compressed copy when there is one, otherwise the original.</param>
/// <param name="ToCompress">The chosen video when it still needs compressing; null otherwise.</param>
public sealed record VideoChoice(string? PlayPath, VideoFileKey? ToCompress);

/// <summary>Picks the video to play from candidates in order of preference (configured video first, then the collection).</summary>
public static class VideoChooser
{
    /// <param name="candidates">Existing <c>.mp4</c> files, most preferred first.</param>
    /// <param name="findPlayable">
    /// The compressed copy of a video, or the video itself when it was found to be compact already;
    /// null when it has not been compressed yet.
    /// </param>
    /// <param name="compress">Whether videos are compressed (<c>video.compress</c>).</param>
    public static VideoChoice Choose(IEnumerable<VideoFileKey> candidates, Func<VideoFileKey, string?> findPlayable, bool compress, ILog log)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.SizeBytes == 0)
            {
                continue;
            }

            if (!compress)
            {
                return new VideoChoice(candidate.FullPath, null);
            }

            if (findPlayable(candidate) is { } playable)
            {
                if (!string.Equals(playable, candidate.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    log.Info($"Playing the compressed copy {playable} of {candidate.FullPath}");
                }

                return new VideoChoice(playable, null);
            }

            return new VideoChoice(candidate.FullPath, candidate);
        }

        return new VideoChoice(null, null);
    }
}

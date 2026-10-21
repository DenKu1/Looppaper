using Looppaper.Core.Logging;

namespace Looppaper.Core.Compression;

/// <summary>
/// The video to play, and the first oversized video met before it (the one to offer to compress).
/// Both can be set: a smaller video plays while the preferred one is too large.
/// </summary>
public sealed record VideoChoice(string? PlayPath, VideoFileKey? Oversized);

/// <summary>Picks the video to play from candidates in order of preference (configured video first, then the collection).</summary>
public static class VideoChooser
{
    /// <param name="candidates">Existing <c>.mp4</c> files, most preferred first.</param>
    /// <param name="maxMb">The video size limit in MB.</param>
    /// <param name="findCompressed">Returns the compressed copy of an oversized video, or null.</param>
    public static VideoChoice Choose(IEnumerable<VideoFileKey> candidates, int maxMb, Func<VideoFileKey, string?> findCompressed, ILog log)
    {
        VideoFileKey? oversized = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate.FullPath) || candidate.SizeBytes == 0)
            {
                continue;
            }

            if (!VideoSizeLimit.IsOversized(candidate.SizeBytes, maxMb))
            {
                return new VideoChoice(candidate.FullPath, oversized);
            }

            if (findCompressed(candidate) is { } copy)
            {
                log.Info($"Video {candidate.FullPath} is {VideoSizeLimit.FormatMb(candidate.SizeBytes)}; playing its compressed copy {copy}");
                return new VideoChoice(copy, oversized);
            }

            log.Warn($"Video {candidate.FullPath} is {VideoSizeLimit.FormatMb(candidate.SizeBytes)}, above the {maxMb} MB video limit, so it will not be played");
            oversized ??= candidate;
        }

        return new VideoChoice(null, oversized);
    }
}

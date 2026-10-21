namespace Looppaper.Core.Tools;

/// <summary>Runs ffmpeg and ffprobe; replaced by a fake in tests so no real ffmpeg is needed.</summary>
public interface IFfmpegExecutor
{
    /// <summary>ffprobe output for <paramref name="file"/>, or null when it cannot be read.</summary>
    MediaInfo? Probe(string file);

    Task<FfmpegResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan? totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>The real ffmpeg found by <see cref="FfmpegLocator"/>.</summary>
public sealed class FfmpegExecutor : IFfmpegExecutor
{
    private readonly FfmpegTool _tool;

    public FfmpegExecutor(FfmpegTool tool)
    {
        _tool = tool;
    }

    public MediaInfo? Probe(string file) => MediaProbe.Probe(_tool, file);

    public Task<FfmpegResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan? totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken cancellationToken) =>
        FfmpegRunner.RunAsync(_tool, arguments, totalDuration, progress, cancellationToken);
}

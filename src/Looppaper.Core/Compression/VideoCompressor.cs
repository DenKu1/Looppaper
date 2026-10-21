using Looppaper.Core.Logging;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Compression;

/// <summary>What to compress and where to put it.</summary>
/// <param name="Source">The original video (never written to).</param>
/// <param name="Info">ffprobe output for the original.</param>
/// <param name="LimitBytes">The result must not be larger.</param>
/// <param name="CacheDirectory">Where the compressed copy goes (<see cref="AppPaths.CacheDirectory"/>).</param>
/// <param name="Screen">Largest monitor; larger videos are scaled down to it.</param>
public sealed record CompressionRequest(VideoFileKey Source, MediaInfo Info, long LimitBytes, string CacheDirectory, PixelSize? Screen);

/// <summary>Outcome of a compression: the copy, or a message saying why there is none.</summary>
public sealed record CompressionResult(string? OutputPath, long OutputBytes, string Message, bool Cancelled = false)
{
    public bool Succeeded => OutputPath is not null;

    public static CompressionResult Failed(string message) => new(null, 0, message);
}

/// <summary>
/// Compresses an oversized video into the cache folder with ffmpeg: plans the bitrate and size, picks a
/// hardware encoder (falling back to the CPU when it fails), checks the result is under the limit (one
/// retry at a lower bitrate), and verifies the copy can be read back. The original is never touched; a
/// partial file is deleted on failure or cancellation.
/// </summary>
public sealed class VideoCompressor
{
    private readonly IFfmpegExecutor _ffmpeg;
    private readonly ILog _log;

    public VideoCompressor(IFfmpegExecutor ffmpeg, ILog log)
    {
        _ffmpeg = ffmpeg;
        _log = log;
    }

    /// <summary>Raised with a short line for the person (encoder chosen, retry, ...).</summary>
    public event Action<string>? Status;

    /// <summary>"Floating In Space-1a2b3c4d5e.mp4": the original's name plus a hash of its path, size and time.</summary>
    public static string OutputFileName(VideoFileKey source)
    {
        var name = Path.GetFileNameWithoutExtension(source.FullPath);
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (name.Length > 60)
        {
            name = name[..60];
        }

        return $"{(name.Length == 0 ? "video" : name)}-{source.ShortHash()}.mp4";
    }

    public async Task<CompressionResult> CompressAsync(CompressionRequest request, IProgress<FfmpegProgress>? progress, CancellationToken cancellationToken)
    {
        var margin = CompressionPlanner.DefaultMargin;
        var plan = CompressionPlanner.Plan(request.Info, request.LimitBytes, request.Screen, margin, out var problem);
        if (plan is null)
        {
            return CompressionResult.Failed($"It cannot be compressed: {problem}.");
        }

        var output = Path.GetFullPath(Path.Combine(request.CacheDirectory, OutputFileName(request.Source)));
        if (string.Equals(output, Path.GetFullPath(request.Source.FullPath), StringComparison.OrdinalIgnoreCase))
        {
            return CompressionResult.Failed("The compressed copy would replace the original; not compressing.");
        }

        var partial = output + ".part";
        try
        {
            Directory.CreateDirectory(request.CacheDirectory);
            var encoder = await EncoderSelector.SelectAsync(_ffmpeg, _log, cancellationToken).ConfigureAwait(false);
            Report($"Encoder: {encoder.Label}");

            for (var attempt = 1; ; attempt++)
            {
                Report($"Compressing to {plan.Describe()}{(attempt > 1 ? " (second try)" : string.Empty)}");
                _log.Info($"Compressing {request.Source.FullPath} -> {output} with {encoder.Name}: {plan.Describe()}");
                var result = await RunAsync(request, plan, encoder, partial, progress, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded && !result.Cancelled && encoder.IsHardware)
                {
                    _log.Warn($"{encoder.Name} failed ({result.ErrorSummary}); retrying with {VideoEncoder.X264.Name}");
                    encoder = VideoEncoder.X264;
                    Report($"The hardware encoder failed; using {encoder.Label} instead");
                    result = await RunAsync(request, plan, encoder, partial, progress, cancellationToken).ConfigureAwait(false);
                }

                if (result.Cancelled || cancellationToken.IsCancellationRequested)
                {
                    Delete(partial);
                    return new CompressionResult(null, 0, "Compression cancelled.", Cancelled: true);
                }

                if (!result.Succeeded)
                {
                    Delete(partial);
                    _log.Error($"ffmpeg failed with exit code {result.ExitCode}: {result.ErrorSummary}");
                    return CompressionResult.Failed($"ffmpeg failed (exit code {result.ExitCode}); see the log for details.");
                }

                var size = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (size == 0)
                {
                    Delete(partial);
                    return CompressionResult.Failed("ffmpeg did not produce a file.");
                }

                if (size <= request.LimitBytes)
                {
                    break;
                }

                if (attempt >= 2)
                {
                    Delete(partial);
                    return CompressionResult.Failed($"The result is still {VideoSizeLimit.FormatMb(size)}, above the limit, after a retry at a lower bitrate.");
                }

                margin = CompressionPlanner.RetryMargin(margin, size, request.LimitBytes);
                plan = CompressionPlanner.Plan(request.Info, request.LimitBytes, request.Screen, margin, out problem);
                if (plan is null)
                {
                    Delete(partial);
                    return CompressionResult.Failed($"The result was {VideoSizeLimit.FormatMb(size)}, above the limit, and a lower bitrate is not possible: {problem}.");
                }

                _log.Warn($"Result was {VideoSizeLimit.FormatMb(size)}, above the limit; retrying with margin {margin:F2}");
                Report($"The result was {VideoSizeLimit.FormatMb(size)}, above the limit; trying again at a lower bitrate");
            }

            if (_ffmpeg.Probe(partial) is not { Width: > 0 } check)
            {
                Delete(partial);
                return CompressionResult.Failed("The compressed copy could not be read back; it was deleted.");
            }

            File.Move(partial, output, overwrite: true);
            var bytes = new FileInfo(output).Length;
            _log.Info($"Compressed {request.Source.FullPath} ({VideoSizeLimit.FormatMb(request.Source.SizeBytes)}) to {output} ({VideoSizeLimit.FormatMb(bytes)}, {check.Width}x{check.Height})");
            return new CompressionResult(output, bytes, $"Compressed to {VideoSizeLimit.FormatMb(bytes)}: {output}");
        }
        catch (OperationCanceledException)
        {
            Delete(partial);
            return new CompressionResult(null, 0, "Compression cancelled.", Cancelled: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Delete(partial);
            _log.Error($"Compressing {request.Source.FullPath} failed", ex);
            return CompressionResult.Failed($"Compression failed: {ex.Message}");
        }
    }

    private Task<FfmpegResult> RunAsync(CompressionRequest request, CompressionPlan plan, VideoEncoder encoder, string partial, IProgress<FfmpegProgress>? progress, CancellationToken cancellationToken) =>
        _ffmpeg.RunAsync(CompressionArguments.Encode(request.Source.FullPath, partial, plan, encoder), plan.Duration, progress, cancellationToken);

    private void Report(string message) => Status?.Invoke(message);

    private void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Cannot delete {path}", ex);
        }
    }
}

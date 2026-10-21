using Looppaper.Core.Logging;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Compression;

/// <summary>What to compress and where to put it.</summary>
/// <param name="Source">The original video (never written to).</param>
/// <param name="Info">ffprobe output for the original.</param>
/// <param name="CacheDirectory">Where the compressed copy goes (<see cref="AppPaths.CacheDirectory"/>).</param>
/// <param name="Screen">Largest monitor; larger videos are scaled down to it.</param>
public sealed record CompressionRequest(VideoFileKey Source, MediaInfo Info, string CacheDirectory, PixelSize? Screen);

/// <summary>Outcome of a compression.</summary>
/// <param name="OutputPath">The compressed copy; null when there is none.</param>
/// <param name="KeepOriginal">True when the original is already compact (compressing would not make it smaller).</param>
public sealed record CompressionResult(string? OutputPath, long OutputBytes, string Message, bool Cancelled = false, bool KeepOriginal = false)
{
    public bool Succeeded => OutputPath is not null;

    /// <summary>The copy is still over <see cref="VideoSize.WarningMb"/>: it plays, but can slow the PC down.</summary>
    public bool IsLarge => Succeeded && VideoSize.IsLarge(OutputBytes);

    public static CompressionResult Failed(string message) => new(null, 0, message);
}

/// <summary>
/// Compresses a video into the cache folder with ffmpeg: scales it down to the screen when it is larger,
/// re-encodes at a wallpaper-friendly bitrate with a hardware encoder (falling back to the CPU when it
/// fails), and verifies the copy can be read back. If the copy would not be smaller the original is kept.
/// The original is never touched; a partial file is deleted on failure or cancellation.
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

    /// <summary>Raised with a short line for the person (encoder chosen, fallback, ...).</summary>
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
        var plan = CompressionPlanner.Plan(request.Info, request.Screen, out var problem);
        if (plan is null)
        {
            return CompressionResult.Failed($"It cannot be compressed: {problem}.");
        }

        if (!plan.Worthwhile)
        {
            _log.Info($"{request.Source.FullPath} is already compact ({VideoSize.FormatMb(request.Source.SizeBytes)}, {plan.Describe()}); keeping the original");
            return new CompressionResult(null, 0, $"{request.Source.FileName} is already compact; playing the original.", KeepOriginal: true);
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
            Report($"Compressing to {plan.Describe()}");
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

            if (size >= request.Source.SizeBytes && !plan.Scaled)
            {
                Delete(partial);
                _log.Info($"Compressing {request.Source.FullPath} gave {VideoSize.FormatMb(size)}, not smaller than the original; keeping the original");
                return new CompressionResult(null, 0, $"{request.Source.FileName} did not get smaller; playing the original.", KeepOriginal: true);
            }

            if (_ffmpeg.Probe(partial) is not { Width: > 0 } check)
            {
                Delete(partial);
                return CompressionResult.Failed("The compressed copy could not be read back; it was deleted.");
            }

            File.Move(partial, output, overwrite: true);
            var bytes = new FileInfo(output).Length;
            _log.Info($"Compressed {request.Source.FullPath} ({VideoSize.FormatMb(request.Source.SizeBytes)}) to {output} ({VideoSize.FormatMb(bytes)}, {check.Width}x{check.Height})");
            return new CompressionResult(output, bytes, $"Compressed {VideoSize.FormatMb(request.Source.SizeBytes)} to {VideoSize.FormatMb(bytes)}: {output}");
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

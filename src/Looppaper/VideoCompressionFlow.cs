using Looppaper.Core;
using Looppaper.Core.Compression;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Tools;
using Looppaper.Desktop;

namespace Looppaper;

/// <summary>Where Looppaper tells the person what it is doing while compressing.</summary>
internal enum CompressionMode
{
    /// <summary>A terminal: status lines and a progress line.</summary>
    Console,

    /// <summary>The background wallpaper process: Windows notifications owned by <c>NotifyWindow</c>.</summary>
    Background,
}

/// <summary>
/// Every video is compressed once before it becomes the wallpaper (<c>video.compress</c>, on by default):
/// scaled down to the screen and re-encoded at a wallpaper-friendly bitrate with ffmpeg (found by
/// <see cref="FfmpegLocator"/>, never bundled) into <see cref="AppPaths.CacheDirectory"/>. The person is told
/// it is happening rather than asked; the original is never changed. Nothing is ever refused: without
/// ffmpeg, or when compression fails, the original plays. A copy still over <see cref="VideoSize.WarningMb"/>
/// plays too, with a warning that it can slow the PC down.
/// </summary>
internal static class VideoCompressionFlow
{
    /// <summary>
    /// What can be played for <paramref name="key"/> without compressing now: its compressed copy, or the
    /// original when it was found to be compact already; null when it has not been compressed yet.
    /// </summary>
    public static string? FindPlayable(VideoFileKey key, ILog log)
    {
        var store = Store(log);
        if (store.FindCompressed(key) is { } copy)
        {
            return copy;
        }

        return store.IsKeptAsIs(key) ? key.FullPath : null;
    }

    /// <summary>
    /// Returns the path to play for <paramref name="videoPath"/>: the compressed copy (compressing it now if
    /// needed, which blocks) or the original. Null only when the file does not exist.
    /// </summary>
    /// <param name="notifyWindow">Background mode: the window that owns the notifications.</param>
    public static string? Resolve(string videoPath, LooppaperConfig config, ILog log, CompressionMode mode, IntPtr notifyWindow = default, CancellationToken cancellationToken = default)
    {
        var key = VideoFileKey.TryFromFile(videoPath);
        if (key is null)
        {
            log.Warn($"Video {videoPath} does not exist");
            return null;
        }

        if (!config.Video.Compress)
        {
            return key.FullPath;
        }

        if (FindPlayable(key, log) is { } playable)
        {
            return playable;
        }

        return Compress(key, config, log, mode, notifyWindow, cancellationToken) ?? key.FullPath;
    }

    /// <summary><c>--compress &lt;file&gt;</c>: compresses (or reports the existing copy) and returns the exit code.</summary>
    public static int RunCompressCommand(string file, LooppaperConfig config, ILog log)
    {
        var path = config.ResolveInCollection(file);
        var key = path is null ? null : VideoFileKey.TryFromFile(path);
        if (key is null)
        {
            ConsoleOutput.WriteLine($"File not found: {path ?? file}");
            return 1;
        }

        if (Store(log).FindCompressed(key) is { } existing)
        {
            ConsoleOutput.WriteLine($"{key.FileName} is already compressed: {existing} ({VideoSize.FormatMb(new FileInfo(existing).Length)}). Looppaper plays that copy.");
            return 0;
        }

        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            return Compress(key, config, log, CompressionMode.Console, IntPtr.Zero, cancel.Token) is null ? 1 : 0;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static CompressionStore Store(ILog log) => new(AppPaths.CompressionStateFile, log);

    /// <summary>Compresses into the cache folder; returns the path to play, or null when it could not compress.</summary>
    private static string? Compress(VideoFileKey key, LooppaperConfig config, ILog log, CompressionMode mode, IntPtr notifyWindow, CancellationToken cancellationToken)
    {
        var console = mode == CompressionMode.Console;
        var tool = new FfmpegLocator().Find(config.FfmpegPath, log);
        if (tool is null || tool.FfprobePath is null)
        {
            var hint = tool is null ? FfmpegLocator.InstallHint : $"ffprobe.exe was not found next to {tool.FfmpegPath}.";
            log.Warn($"Not compressing {key.FullPath}: {hint}");
            var store = Store(log);
            if (console)
            {
                ConsoleOutput.WriteLine(CompressionMessages.FfmpegMissing(key, hint));
            }
            else if (!store.WasToldFfmpegMissing(key))
            {
                store.SetToldFfmpegMissing(key);
                TrayNotification.Show(notifyWindow, CompressionMessages.Title, CompressionMessages.FfmpegMissing(key, hint), warning: false, log);
            }

            return null;
        }

        if (MediaProbe.Probe(tool, key.FullPath) is not { } info)
        {
            log.Warn($"Not compressing {key.FullPath}: ffprobe could not read it");
            if (console)
            {
                ConsoleOutput.WriteLine($"{key.FileName} could not be read by ffprobe; it plays as it is.");
            }

            return null;
        }

        var (width, height) = Monitors.Largest();
        var screen = new PixelSize(width, height);
        var plan = CompressionPlanner.Plan(info, screen, out var problem);
        if (plan is null)
        {
            log.Warn($"Not compressing {key.FullPath}: {problem}");
            if (console)
            {
                ConsoleOutput.WriteLine($"{key.FileName} cannot be compressed ({problem}); it plays as it is.");
            }

            return null;
        }

        log.Info($"Using ffmpeg {tool.Version} at {tool.FfmpegPath}");
        if (console)
        {
            ConsoleOutput.WriteLine(CompressionMessages.Starting(key, plan, AppPaths.CacheDirectory));
            if (plan.Worthwhile)
            {
                ConsoleOutput.WriteLine("Ctrl+C cancels; the original is not changed.");
            }
        }
        else if (plan.Worthwhile)
        {
            TrayNotification.Show(notifyWindow, CompressionMessages.Title, CompressionMessages.StartingInBackground(key), warning: false, log);
        }

        var compressor = new VideoCompressor(new FfmpegExecutor(tool), log);
        if (console)
        {
            compressor.Status += ConsoleOutput.WriteLine;
        }

        var request = new CompressionRequest(key, info, AppPaths.CacheDirectory, screen);
        var result = compressor.CompressAsync(request, new ProgressReport(console, log), cancellationToken).GetAwaiter().GetResult();
        if (console)
        {
            ConsoleOutput.WriteLine(result.Message);
        }

        if (result.KeepOriginal)
        {
            Store(log).RecordKeepOriginal(key);
            return key.FullPath;
        }

        if (!result.Succeeded)
        {
            log.Warn($"Compressing {key.FullPath} did not succeed: {result.Message}");
            if (!console && !result.Cancelled)
            {
                TrayNotification.Show(notifyWindow, CompressionMessages.Title, $"Compressing {key.FileName} failed; it plays as it is. Details are in {AppPaths.LogFile}.", warning: true, log);
            }

            return null;
        }

        Store(log).RecordCompressed(key, result.OutputPath!);
        if (result.IsLarge)
        {
            var warning = CompressionMessages.LargeWarning(key.FileName, result.OutputBytes);
            log.Warn(warning);
            if (console)
            {
                ConsoleOutput.WriteLine("Warning: " + warning);
            }
            else
            {
                TrayNotification.Show(notifyWindow, CompressionMessages.Title, warning, warning: true, log);
            }
        }

        return result.OutputPath;
    }

    /// <summary>ffmpeg progress: a live line in the terminal, every 25% in the log.</summary>
    private sealed class ProgressReport(bool console, ILog log) : IProgress<FfmpegProgress>
    {
        private int _loggedQuarter;

        public void Report(FfmpegProgress value)
        {
            if (console)
            {
                ConsoleOutput.WriteProgress($"  {value}");
            }

            var quarter = value.Fraction is { } f ? (int)(f * 4) : 0;
            if (quarter > _loggedQuarter && !value.Finished)
            {
                _loggedQuarter = quarter;
                log.Info($"Compression {value}");
            }
        }
    }
}

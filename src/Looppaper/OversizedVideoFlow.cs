using Looppaper.Core;
using Looppaper.Core.Compression;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Tools;
using Looppaper.Desktop;
using Looppaper.Native;

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
/// What happens to a video above the size limit (<c>video.maxFileSizeMb</c>, at most 100 MB): it is not
/// played. Looppaper tells the person (file name, size, estimated result) and asks before compressing it
/// with ffmpeg (found by <see cref="FfmpegLocator"/>, never bundled) into <see cref="AppPaths.CacheDirectory"/>;
/// it never compresses silently and never changes the original. A "no" is remembered per file version in
/// <see cref="AppPaths.CompressionStateFile"/> so the background wallpaper does not ask on every start.
/// </summary>
internal static class OversizedVideoFlow
{
    /// <summary>
    /// Returns a path that can be played for <paramref name="videoPath"/>, or null to show pictures instead:
    /// the file itself when it is within the size limit; otherwise, after asking the person (console prompt
    /// or message box, see <paramref name="mode"/>), a compressed copy. Blocks while asking and compressing,
    /// so call it from a command or a worker thread. In <see cref="OversizedVideoMode.Background"/> a file
    /// the person already said no to is not asked about again; in <see cref="OversizedVideoMode.Console"/>
    /// the person is always asked.
    /// </summary>
    /// <param name="videoPath">Full path of the chosen video (e.g. a Wallpaper Engine item's .mp4).</param>
    /// <param name="config">Loaded config (size limit, ffmpeg path).</param>
    /// <param name="log">Log for what was decided.</param>
    /// <param name="mode">Whether a person at a terminal or a message box is asked.</param>
    /// <param name="cancellationToken">Stops a running compression (the partial copy is deleted).</param>
    public static string? Resolve(string videoPath, LooppaperConfig config, ILog log, OversizedVideoMode mode, CancellationToken cancellationToken = default)
    {
        var key = VideoFileKey.TryFromFile(videoPath);
        if (key is null)
        {
            log.Warn($"Video {videoPath} does not exist");
            return null;
        }

        var choice = VideoChooser.Choose([key], config.Video.MaxFileSizeMb, k => FindCompressed(k, config, log), log);
        if (choice.PlayPath is not null || choice.Oversized is null)
        {
            return choice.PlayPath;
        }

        if (mode == OversizedVideoMode.Background && Store(log).IsDeclined(key))
        {
            log.Info($"Not offering to compress {key.FullPath}: the answer was no earlier");
            return null;
        }

        var offer = Prepare(key, config, log, mode);
        return offer is not null && Ask(offer, log, mode) == true ? Compress(offer, log, mode, cancellationToken) : null;
    }

    /// <summary>The compressed copy of an oversized video, or null when there is none.</summary>
    public static string? FindCompressed(VideoFileKey key, LooppaperConfig config, ILog log) => null;

    /// <summary><c>--compress &lt;file&gt;</c>: shows the estimate, always asks, compresses on yes. Returns the exit code.</summary>
    public static int RunCompressCommand(string file, LooppaperConfig config, ILog log)
    {
        var path = config.ResolveInCollection(file);
        var key = path is null ? null : VideoFileKey.TryFromFile(path);
        if (key is null)
        {
            ConsoleOutput.WriteLine($"File not found: {path ?? file}");
            return 1;
        }

        var maxMb = config.Video.MaxFileSizeMb;
        if (!VideoSizeLimit.IsOversized(key.SizeBytes, maxMb))
        {
            ConsoleOutput.WriteLine($"{key.FileName} is {VideoSizeLimit.FormatMb(key.SizeBytes)}, within the {maxMb} MB video limit; nothing to compress.");
            return 0;
        }

        if (FindCompressed(key, config, log) is { } existing)
        {
            ConsoleOutput.WriteLine($"{key.FileName} is already compressed: {existing}. Looppaper plays that copy instead.");
            return 0;
        }

        log.Info($"--compress: {key.FullPath} is {VideoSizeLimit.FormatMb(key.SizeBytes)}");
        var offer = Prepare(key, config, log, OversizedVideoMode.Console);
        if (offer is null)
        {
            return 1;
        }

        switch (Ask(offer, log, OversizedVideoMode.Console))
        {
            case null:
                return 1;
            case false:
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
            return Compress(offer, log, OversizedVideoMode.Console, cancel.Token) is null ? 1 : 0;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static CompressionStore Store(ILog log) => new(AppPaths.CompressionStateFile, log);

    /// <summary>Finds ffmpeg, probes the video and plans the result; reports and returns null when that is not possible.</summary>
    private static Offer? Prepare(VideoFileKey key, LooppaperConfig config, ILog log, OversizedVideoMode mode)
    {
        var maxMb = config.Video.MaxFileSizeMb;
        var tool = new FfmpegLocator().Find(config.FfmpegPath, log);
        if (tool is null)
        {
            log.Warn($"Cannot offer to compress {key.FullPath}: ffmpeg was not found");
            if (mode == OversizedVideoMode.Console)
            {
                ConsoleOutput.WriteLine(CompressionPrompt.TooLarge(key, maxMb));
                ConsoleOutput.WriteLine(FfmpegLocator.InstallHint);
            }
            else
            {
                var store = Store(log);
                if (!store.WasToldFfmpegMissing(key))
                {
                    store.SetToldFfmpegMissing(key);
                    User32.MessageBox(IntPtr.Zero, CompressionPrompt.FfmpegMissingText(key, maxMb, FfmpegLocator.InstallHint), CompressionPrompt.Title, User32.MB_ICONINFORMATION | User32.MB_SETFOREGROUND | User32.MB_TOPMOST);
                }
            }

            return null;
        }

        log.Info($"Using ffmpeg {tool.Version} at {tool.FfmpegPath}");
        string? problem;
        CompressionPlan? plan = null;
        if (tool.FfprobePath is null)
        {
            problem = $"ffprobe.exe was not found next to {tool.FfmpegPath}";
        }
        else if (MediaProbe.Probe(tool, key.FullPath) is not { } info)
        {
            problem = "ffprobe could not read it";
        }
        else
        {
            var (width, height) = Monitors.Largest();
            plan = CompressionPlanner.Plan(info, VideoSizeLimit.Bytes(maxMb), new PixelSize(width, height), out problem);
            if (plan is not null)
            {
                return new Offer(key, maxMb, tool, info, plan, new PixelSize(width, height));
            }
        }

        log.Warn($"Cannot compress {key.FullPath}: {problem}");
        if (mode == OversizedVideoMode.Console)
        {
            ConsoleOutput.WriteLine(CompressionPrompt.TooLarge(key, maxMb));
            ConsoleOutput.WriteLine($"It cannot be compressed: {problem}.");
        }

        return null;
    }

    /// <summary>Asks the person and remembers the answer. True means compress; null means nobody could answer.</summary>
    private static bool? Ask(Offer offer, ILog log, OversizedVideoMode mode)
    {
        bool yes;
        if (mode == OversizedVideoMode.Console)
        {
            ConsoleOutput.WriteLine(CompressionPrompt.Details(offer.Key, offer.MaxMb, offer.Plan, AppPaths.CacheDirectory));
            var answer = ConsoleOutput.Prompt(CompressionPrompt.ConsoleQuestion(offer.MaxMb));
            if (answer is null)
            {
                ConsoleOutput.WriteLine("Nobody can answer here (not an interactive terminal); not compressing.");
                log.Info($"Not compressing {offer.Key.FullPath}: no interactive terminal to ask");
                return null;
            }

            yes = CompressionPrompt.IsYes(answer);
        }
        else
        {
            var text = CompressionPrompt.MessageBoxText(offer.Key, offer.MaxMb, offer.Plan, AppPaths.CacheDirectory);
            yes = User32.MessageBox(IntPtr.Zero, text, CompressionPrompt.Title, User32.MB_YESNO | User32.MB_ICONQUESTION | User32.MB_DEFBUTTON2 | User32.MB_SETFOREGROUND | User32.MB_TOPMOST) == User32.IDYES;
        }

        Store(log).SetDeclined(offer.Key, !yes);
        log.Info($"Compress {offer.Key.FullPath} ({offer.Plan.Describe()})? {(yes ? "yes" : "no")}");
        if (!yes && mode == OversizedVideoMode.Console)
        {
            ConsoleOutput.WriteLine("Not compressing. Looppaper will not ask about this file at start-up again; run --compress to compress it later.");
        }

        return yes;
    }

    /// <summary>Compresses into the cache folder; returns the copy, or null after reporting the failure.</summary>
    private static string? Compress(Offer offer, ILog log, OversizedVideoMode mode, CancellationToken cancellationToken)
    {
        var console = mode == OversizedVideoMode.Console;
        var compressor = new VideoCompressor(new FfmpegExecutor(offer.Tool), log);
        if (console)
        {
            compressor.Status += ConsoleOutput.WriteLine;
            ConsoleOutput.WriteLine("Compressing (Ctrl+C cancels; the original is not changed).");
        }

        var request = new CompressionRequest(offer.Key, offer.Info, VideoSizeLimit.Bytes(offer.MaxMb), AppPaths.CacheDirectory, offer.Screen);
        var result = compressor.CompressAsync(request, new ProgressReport(console, log), cancellationToken).GetAwaiter().GetResult();
        if (console)
        {
            ConsoleOutput.WriteLine(result.Message);
        }

        if (!result.Succeeded)
        {
            log.Warn($"Compressing {offer.Key.FullPath} did not succeed: {result.Message}");
            if (!console && !result.Cancelled)
            {
                User32.MessageBox(
                    IntPtr.Zero,
                    $"Compressing {offer.Key.FullPath} failed.{Environment.NewLine}{Environment.NewLine}{result.Message}{Environment.NewLine}{Environment.NewLine}Pictures are shown instead. Details are in {AppPaths.LogFile}.",
                    CompressionPrompt.Title,
                    User32.MB_ICONWARNING | User32.MB_SETFOREGROUND | User32.MB_TOPMOST);
            }

            return null;
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

    private sealed record Offer(VideoFileKey Key, int MaxMb, FfmpegTool Tool, MediaInfo Info, CompressionPlan Plan, PixelSize Screen);
}

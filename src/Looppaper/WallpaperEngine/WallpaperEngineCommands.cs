using System.Diagnostics;
using System.Text;
using Looppaper.Core;
using Looppaper.Core.CommandLine;
using Looppaper.Core.Compression;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Tools;
using Looppaper.Core.WallpaperEngine;

namespace Looppaper.WallpaperEngine;

/// <summary>The <c>--we-*</c> console commands for wallpapers from the Wallpaper Engine Workshop.</summary>
internal sealed class WallpaperEngineCommands
{
    private readonly CommandLineOptions _options;
    private readonly LooppaperConfig _config;
    private readonly string _configPath;
    private readonly ILog _log;

    public WallpaperEngineCommands(CommandLineOptions options, LooppaperConfig config, string configPath, ILog log)
    {
        _options = options;
        _config = config;
        _configPath = configPath;
        _log = log;
    }

    public int Run()
    {
        var location = SteamRegistry.Locate(_config, _log);
        if (location.WorkshopFolders.Count == 0)
        {
            ConsoleOutput.WriteLine(
                "No Wallpaper Engine Workshop folder was found. Install Wallpaper Engine from Steam and subscribe to some " +
                "wallpapers, or set \"wallpaperEngine\": { \"workshopPath\": \"...\\\\steamapps\\\\workshop\\\\content\\\\431960\" } in " + _configPath + ".");
            return 1;
        }

        // Recordings from before video.folder are still found in the cache; they are moved to the video folder here.
        var items = WorkshopLibrary.Scan(location.WorkshopFolders, VideoFolders, _log);
        if (MoveLegacyRecordings(items) > 0)
        {
            items = WorkshopLibrary.Scan(location.WorkshopFolders, VideoFolders, _log);
        }

        _log.Info($"Wallpaper Engine: {items.Count} Workshop items in {string.Join("; ", location.WorkshopFolders)}; install {location.InstallFolder ?? "not found"}");

        if (_options.WeList)
        {
            return List(items, location);
        }

        if (items.Count == 0)
        {
            ConsoleOutput.WriteLine($"No wallpapers in {string.Join("; ", location.WorkshopFolders)}.");
            return 1;
        }

        WorkshopItem? item;
        if (_options.WePick)
        {
            List(items, location);
            if (!ConsoleOutput.IsInteractive)
            {
                ConsoleOutput.WriteLine("--we-pick needs a terminal to ask in. Use --we-set <number|id> instead.");
                return 2;
            }

            var answer = ConsoleOutput.Prompt($"Wallpaper number or Workshop id (1-{items.Count}, Enter to cancel): ");
            if (string.IsNullOrWhiteSpace(answer))
            {
                ConsoleOutput.WriteLine("Nothing changed.");
                return 0;
            }

            item = WorkshopLibrary.Select(items, answer);
            if (item is null)
            {
                ConsoleOutput.WriteLine($"'{answer.Trim()}' is not a number from 1 to {items.Count} or a Workshop id in the list.");
                return 2;
            }
        }
        else
        {
            var wanted = _options.WeSet ?? _options.WeConvert!;
            item = WorkshopLibrary.Select(items, wanted);
            if (item is null)
            {
                ConsoleOutput.WriteLine($"'{wanted}' is not a number from 1 to {items.Count} or a Workshop id. See --we-list.");
                return 2;
            }
        }

        if (_options.WeConvert is not null)
        {
            return ConvertOnly(item, location);
        }

        return Use(item, location);
    }

    /// <summary>Where videos made from Workshop items are looked for: the video folder, then the old cache folder.</summary>
    private IReadOnlyList<string> VideoFolders => [_config.ResolveVideoFolder(), AppPaths.CacheDirectory];

    /// <summary>
    /// Moves recordings in the old naming (<c>we-&lt;id&gt;-&lt;time&gt;.mp4</c> in the cache) to the video folder with a
    /// readable name, so they stay in the collection. <c>video.file</c> and the compression records follow them.
    /// Returns how many were moved.
    /// </summary>
    private int MoveLegacyRecordings(IReadOnlyList<WorkshopItem> items)
    {
        var legacy = ConversionCache.Legacy(AppPaths.CacheDirectory);
        if (legacy.Count == 0)
        {
            return 0;
        }

        var folder = _config.ResolveVideoFolder();
        var configured = _config.ResolveVideo();
        var moved = 0;
        foreach (var from in legacy)
        {
            var tag = ConversionCache.TagOf(from)!;
            var item = items.FirstOrDefault(i => string.Equals(ConversionCache.Tag(i.Id, i.ModifiedUtc), tag, StringComparison.OrdinalIgnoreCase));
            var to = Path.Combine(folder, ConversionCache.FileName(item?.Title ?? string.Empty, tag));
            if (!MoveVideo(from, to))
            {
                continue;
            }

            moved++;
            ConsoleOutput.WriteLine($"Moved {Path.GetFileName(from)} to the video folder: {to}");
            if (string.Equals(configured, from, StringComparison.OrdinalIgnoreCase))
            {
                _config.Video.File = to;
                if (ConfigEditor.SetVideoFile(_configPath, to) is { } error)
                {
                    _log.Warn($"Pointing video.file at {to} failed: {error}");
                }
            }
        }

        return moved;
    }

    /// <summary>Moves a video (keeping its write time, so compression records still match) and moves its records along.</summary>
    private bool MoveVideo(string from, string to)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (File.Exists(to))
            {
                // Same recording already there (an earlier move was interrupted): keep that one.
                File.Delete(from);
            }
            else
            {
                File.Move(from, to);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Moving {from} to {to} failed: {ex.Message}");
            return false;
        }

        new CompressionStore(AppPaths.CompressionStateFile, _log).Rename(from, to);
        _log.Info($"Moved {from} to {to}");
        return true;
    }

    /// <summary>
    /// Moves the compressed copy of a video item to the video folder, or copies the original there when it plays as it
    /// is. The copy is recorded as compact, so the wallpaper does not compress it again. Returns the copy, or the
    /// original when saving failed.
    /// </summary>
    private string SaveToVideoFolder(WorkshopItem item, string playable)
    {
        var original = item.FilePath!;
        var target = ConversionCache.PathFor(_config.ResolveVideoFolder(), item.Title, item.Id, item.ModifiedUtc);
        var store = new CompressionStore(AppPaths.CompressionStateFile, _log);
        var originalKey = VideoFileKey.TryFromFile(original);
        var compact = !string.Equals(playable, original, StringComparison.OrdinalIgnoreCase)
            || (originalKey is not null && store.IsKeptAsIs(originalKey));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (compact && !string.Equals(playable, original, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(playable, target, overwrite: true);
                store.Rename(playable, target);
            }
            else
            {
                File.Copy(original, target, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Saving Workshop item {item.Id} to {target} failed: {ex.Message}");
            ConsoleOutput.WriteLine($"Could not save it in the video folder ({ex.Message}); using it from the Workshop folder.");
            return original;
        }

        if (compact && VideoFileKey.TryFromFile(target) is { } targetKey)
        {
            store.RecordKeepOriginal(targetKey);
        }

        DeleteStale(item);
        _log.Info($"Workshop item {item.Id} saved as {target}");
        ConsoleOutput.WriteLine($"Saved in the video folder: {target}");
        return target;
    }

    /// <summary>Deletes the videos made from older versions of the item.</summary>
    private void DeleteStale(WorkshopItem item)
    {
        foreach (var stale in ConversionCache.Stale(VideoFolders, item.Id, item.ModifiedUtc))
        {
            try
            {
                File.Delete(stale);
                _log.Info($"Deleted {stale}, made from an older version of Workshop item {item.Id}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static int List(IReadOnlyList<WorkshopItem> items, WallpaperEngineLocation location)
    {
        if (items.Count == 0)
        {
            ConsoleOutput.WriteLine($"No wallpapers in {string.Join("; ", location.WorkshopFolders)}.");
            return 0;
        }

        ConsoleOutput.WriteLine(string.Join(Environment.NewLine, WorkshopLibrary.FormatTable(items)));
        ConsoleOutput.WriteLine(string.Empty);
        ConsoleOutput.WriteLine($"{items.Count} wallpapers in {string.Join("; ", location.WorkshopFolders)}");
        return 0;
    }

    /// <summary>Makes the item playable if needed, writes it to config.json and restarts Looppaper.</summary>
    private int Use(WorkshopItem item, WallpaperEngineLocation location)
    {
        ConsoleOutput.WriteLine($"{item.Number}. {item.Title} ({WorkshopLibrary.TypeText(item.Type)}, {item.Id}): {WorkshopLibrary.StatusText(item.Status)}");
        var play = PlayableFile(item, location);
        if (play is null)
        {
            return 1;
        }

        var error = ConfigEditor.SetVideo(_configPath, play);
        if (error is not null)
        {
            _log.Error($"Updating config for Workshop item {item.Id} failed: {error}");
            ConsoleOutput.WriteLine($"config.json was not changed: {error}");
            return 1;
        }

        _log.Info($"Wallpaper Engine item {item.Id} ({item.Title}) set as the video: {play}");
        ConsoleOutput.WriteLine($"Set as the wallpaper video in {_configPath}:");
        ConsoleOutput.WriteLine($"  {play}");
        return _options.NoRestart ? 0 : Restart();
    }

    /// <summary>The .mp4 to play for the item, or null (after explaining why) when there is none.</summary>
    private string? PlayableFile(WorkshopItem item, WallpaperEngineLocation location)
    {
        switch (item.Status)
        {
            case WorkshopItemStatus.Converted:
                return item.PlayableFile;
            case WorkshopItemStatus.Ready:
                return ResolveVideo(item);
            case WorkshopItemStatus.NeedsConversion:
                ConsoleOutput.WriteLine($"This is a {WorkshopLibrary.TypeText(item.Type)} wallpaper; it has to be recorded to a video before Looppaper can play it.");
                return Convert(item, location, ask: true);
            default:
                ConsoleOutput.WriteLine($"Looppaper cannot play this wallpaper: {item.Note ?? "unsupported"}.");
                return null;
        }
    }

    /// <summary>
    /// A video item: compressed here, with progress (unless <c>video.compress</c> is off), and saved in the video
    /// folder with a readable name, so it stays in the collection. Returns that copy; null only when the file is
    /// gone or the person cancelled.
    /// </summary>
    private string? ResolveVideo(WorkshopItem item)
    {
        if (item.ConvertedFile is { } existing)
        {
            ConsoleOutput.WriteLine($"Already in the video folder: {existing}");
            return existing;
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
            var playable = VideoCompressionFlow.Resolve(item.FilePath!, _config, _log, CompressionMode.Console, cancellationToken: cancel.Token);
            if (playable is null)
            {
                ConsoleOutput.WriteLine("config.json was not changed.");
                return null;
            }

            return SaveToVideoFolder(item, playable);
        }
        catch (OperationCanceledException)
        {
            ConsoleOutput.WriteLine("Cancelled; config.json was not changed.");
            return null;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary><c>--we-convert</c>: records a scene / web item into the cache without changing config.json.</summary>
    private int ConvertOnly(WorkshopItem item, WallpaperEngineLocation location)
    {
        ConsoleOutput.WriteLine($"{item.Number}. {item.Title} ({WorkshopLibrary.TypeText(item.Type)}, {item.Id}): {WorkshopLibrary.StatusText(item.Status)}");
        switch (item.Status)
        {
            case WorkshopItemStatus.Converted:
                ConsoleOutput.WriteLine($"Already converted: {item.ConvertedFile}");
                ConsoleOutput.WriteLine($"Use it with: Looppaper.exe --we-set {item.Id}");
                return 0;
            case WorkshopItemStatus.NeedsConversion:
                if (Convert(item, location, ask: false) is null)
                {
                    return 1;
                }

                ConsoleOutput.WriteLine($"Use it with: Looppaper.exe --we-set {item.Id}");
                return 0;
            case WorkshopItemStatus.Ready:
                ConsoleOutput.WriteLine("This is a video wallpaper; it needs no conversion. Use it with --we-set.");
                return 0;
            default:
                ConsoleOutput.WriteLine($"Looppaper cannot play this wallpaper: {item.Note ?? "unsupported"}.");
                return 1;
        }
    }

    /// <summary>
    /// Records a scene / web item to a looping .mp4 in the video folder (asking first when <paramref name="ask"/>);
    /// returns its path, or null after explaining what went wrong.
    /// </summary>
    private string? Convert(WorkshopItem item, WallpaperEngineLocation location, bool ask)
    {
        if (location.WallpaperExe is null)
        {
            ConsoleOutput.WriteLine(
                "Converting needs Wallpaper Engine, but wallpaper64.exe was not found. Install it from Steam, or set " +
                $"\"wallpaperEngine\": {{ \"installPath\": \"...\\\\steamapps\\\\common\\\\wallpaper_engine\" }} in {_configPath}.");
            return null;
        }

        var ffmpeg = new FfmpegLocator().Find(_config.FfmpegPath, _log, minimumMajor: 8);
        if (ffmpeg is null)
        {
            ConsoleOutput.WriteLine("Converting needs ffmpeg 8 or newer (for its gfxcapture window recorder). " + FfmpegLocator.InstallHint);
            return null;
        }

        var filters = FfmpegProcess.Run(ffmpeg.FfmpegPath, ["-hide_banner", "-h", "filter=gfxcapture"], TimeSpan.FromSeconds(30));
        if (!Encoding.UTF8.GetString(filters.StandardOutput).Contains("gfxcapture AVOptions", StringComparison.Ordinal))
        {
            ConsoleOutput.WriteLine($"The ffmpeg at {ffmpeg.FfmpegPath} ({ffmpeg.Version}) has no gfxcapture window recorder. " + FfmpegLocator.InstallHint);
            return null;
        }

        var converter = new SceneConverter(ffmpeg, location.WallpaperExe, _log, ConsoleOutput.WriteLine);
        var plan = CapturePlan.From(converter.FindLoop(item));
        var minutes = Math.Max(1, (int)Math.Ceiling(SceneCapture.Estimate(plan).TotalMinutes));
        ConsoleOutput.WriteLine(
            $"Converting records {plan.Seconds:0.#} s of it ({plan.Reason}) into a video (aiming for under {VideoSize.WarningMb} MB). " +
            $"It takes about {minutes} min: Wallpaper Engine plays it in an off-screen window while ffmpeg {ffmpeg.Version} records it. " +
            "If Wallpaper Engine isn't running, it is started for the recording (it may show its own wallpaper meanwhile) and closed again afterwards.");

        if (ask)
        {
            if (!ConsoleOutput.IsInteractive)
            {
                ConsoleOutput.WriteLine($"Run Looppaper.exe --we-convert {item.Id} from a terminal to convert it.");
                return null;
            }

            var answer = ConsoleOutput.Prompt("Convert now? [y/N] ")?.Trim();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleOutput.WriteLine("Nothing changed.");
                return null;
            }
        }

        var output = ConversionCache.PathFor(_config.ResolveVideoFolder(), item.Title, item.Id, item.ModifiedUtc);
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            converter.ConvertAsync(item, plan, output, new ConsoleProgress(), cancel.Token).GetAwaiter().GetResult();
        }
        catch (ConversionException ex)
        {
            ConsoleOutput.WriteLine($"Converting failed: {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            _log.Info($"Converting Workshop item {item.Id} was cancelled");
            ConsoleOutput.WriteLine("Cancelled; nothing was saved.");
            return null;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        DeleteStale(item);
        ConsoleOutput.WriteLine($"Converted: {output} ({WorkshopLibrary.SizeText(new FileInfo(output).Length)})");
        return output;
    }

    /// <summary>Prints ffmpeg progress in 10% steps.</summary>
    private sealed class ConsoleProgress : IProgress<FfmpegProgress>
    {
        private readonly object _lock = new();
        private int _lastStep = -1;

        public void Report(FfmpegProgress value)
        {
            if (value.Fraction is not { } fraction)
            {
                return;
            }

            var step = (int)(fraction * 10);
            lock (_lock)
            {
                if (step <= _lastStep)
                {
                    return;
                }

                _lastStep = step;
            }

            ConsoleOutput.WriteLine($"  {value}");
        }
    }

    /// <summary>Stops a running Looppaper and starts it again in the background with the new config.</summary>
    private int Restart()
    {
        var stopped = SingleInstance.SignalStop(TimeSpan.FromSeconds(10));
        var startCommand = "Looppaper.exe" + (_options.ConfigPath is null ? string.Empty : $" --config \"{_configPath}\"");
        switch (stopped)
        {
            case SingleInstance.StopResult.NotRunning:
                ConsoleOutput.WriteLine($"Looppaper is not running. Start it with: {startCommand}");
                return 0;
            case SingleInstance.StopResult.TimedOut:
                ConsoleOutput.WriteLine($"The running Looppaper did not stop within 10 seconds. Restart it yourself: Looppaper.exe --stop, then {startCommand}");
                return 1;
        }

        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            ConsoleOutput.WriteLine($"Looppaper stopped; start it again with: {startCommand}");
            return 1;
        }

        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        if (_options.ConfigPath is not null)
        {
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(_configPath);
        }

        start.ArgumentList.Add("--background");
        try
        {
            using var child = Process.Start(start);
            if (child is null || child.WaitForExit(1500))
            {
                ConsoleOutput.WriteLine($"Looppaper did not start again; see {AppPaths.LogFile}");
                return 1;
            }

            _log.Info($"Restarted Looppaper for the new video (pid {child.Id})");
            ConsoleOutput.WriteLine($"Looppaper restarted with the new wallpaper (pid {child.Id}).");
            return 0;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Error("Restarting Looppaper failed", ex);
            ConsoleOutput.WriteLine($"Restarting Looppaper failed: {ex.Message}. Start it with: {startCommand}");
            return 1;
        }
    }
}

using System.Diagnostics;
using Looppaper.Core;
using Looppaper.Core.CommandLine;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
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

        var items = WorkshopLibrary.Scan(location.WorkshopFolders, AppPaths.CacheDirectory, _log);
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
            item = WorkshopLibrary.Select(items, _options.WeSet!);
            if (item is null)
            {
                ConsoleOutput.WriteLine($"'{_options.WeSet}' is not a number from 1 to {items.Count} or a Workshop id. See --we-list.");
                return 2;
            }
        }

        return Use(item, location);
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
            case WorkshopItemStatus.Ready:
            case WorkshopItemStatus.Converted:
                return item.PlayableFile;
            case WorkshopItemStatus.TooLarge:
                ConsoleOutput.WriteLine($"This video is {WorkshopLibrary.SizeText(item.SizeBytes)}, over the {VideoConfig.MaxFileSizeCapMb} MB video limit, so it cannot be played as it is.");
                return null;
            case WorkshopItemStatus.NeedsConversion:
                ConsoleOutput.WriteLine($"This is a {WorkshopLibrary.TypeText(item.Type)} wallpaper; it has to be recorded to a video before Looppaper can play it.");
                return null;
            default:
                ConsoleOutput.WriteLine($"Looppaper cannot play this wallpaper: {item.Note ?? "unsupported"}.");
                return null;
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

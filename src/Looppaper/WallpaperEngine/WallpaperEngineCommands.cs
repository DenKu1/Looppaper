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
        return List(items, location);
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
}

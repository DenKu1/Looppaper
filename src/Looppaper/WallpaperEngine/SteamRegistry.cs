using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.WallpaperEngine;
using Microsoft.Win32;

namespace Looppaper.WallpaperEngine;

/// <summary>Reads Steam's install folder from the registry and locates Wallpaper Engine with it.</summary>
internal static class SteamRegistry
{
    private const string SteamKey = @"Software\Valve\Steam";

    /// <summary>The <c>SteamPath</c> value (e.g. <c>c:/program files (x86)/steam</c>), or null when Steam is not installed.</summary>
    public static string? ReadSteamPath(ILog log)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SteamKey);
            return key?.GetValue("SteamPath") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            log.Warn($"Cannot read HKCU\\{SteamKey}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Finds the Workshop folders and the Wallpaper Engine install for this user.</summary>
    public static WallpaperEngineLocation Locate(LooppaperConfig config, ILog log) =>
        WorkshopLocator.Locate(config.WallpaperEngine, ReadSteamPath(log), log);
}

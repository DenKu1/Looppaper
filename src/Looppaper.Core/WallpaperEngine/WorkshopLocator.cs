using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;

namespace Looppaper.Core.WallpaperEngine;

/// <summary>Where Wallpaper Engine's Workshop downloads and its install folder were found.</summary>
/// <param name="WorkshopFolders">Existing <c>...\workshop\content\431960</c> folders, configured one first.</param>
/// <param name="InstallFolder">The <c>wallpaper_engine</c> folder with <c>wallpaper64.exe</c>, or null.</param>
public sealed record WallpaperEngineLocation(IReadOnlyList<string> WorkshopFolders, string? InstallFolder)
{
    /// <summary>Full path of <c>wallpaper64.exe</c>, or null when Wallpaper Engine was not found.</summary>
    public string? WallpaperExe => InstallFolder is null ? null : Path.Combine(InstallFolder, WorkshopLocator.WallpaperExeName);
}

/// <summary>
/// Finds the Steam libraries (Steam's own folder plus every <c>"path"</c> in
/// <c>steamapps\libraryfolders.vdf</c>), the Wallpaper Engine Workshop folders in them and the
/// Wallpaper Engine install. The <c>wallpaperEngine</c> config section overrides both.
/// </summary>
public static class WorkshopLocator
{
    /// <summary>Wallpaper Engine's Steam app id.</summary>
    public const string AppId = "431960";

    public const string WallpaperExeName = "wallpaper64.exe";

    /// <summary>Steam library folders, Steam's own folder first, without duplicates.</summary>
    /// <param name="steamPath">The <c>SteamPath</c> registry value (may use forward slashes), or null.</param>
    public static IReadOnlyList<string> SteamLibraries(string? steamPath, ILog log)
    {
        var libraries = new List<string>();
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return libraries;
        }

        var steam = Normalize(steamPath);
        if (steam is null)
        {
            return libraries;
        }

        libraries.Add(steam);
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf))
            {
                libraries.AddRange(LibraryPaths(File.ReadAllText(vdf)).Select(Normalize).OfType<string>());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Cannot read {vdf}: {ex.Message}");
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Library paths listed in <c>libraryfolders.vdf</c> (current and pre-2021 layouts).</summary>
    public static IEnumerable<string> LibraryPaths(string vdfText)
    {
        var root = KeyValues.Parse(vdfText);
        var folders = root["libraryfolders"] ?? root.Children.FirstOrDefault();
        if (folders is null)
        {
            yield break;
        }

        foreach (var entry in folders.Children)
        {
            if (entry.Value is null)
            {
                // "0" { "path" "D:\\SteamLibrary" ... }
                if (entry["path"]?.Value is { Length: > 0 } path)
                {
                    yield return path;
                }
            }
            else if (int.TryParse(entry.Name, out _) && entry.Value.Length > 0)
            {
                // Old layout: "1" "D:\\SteamLibrary"
                yield return entry.Value;
            }
        }
    }

    /// <summary>Finds the Workshop folders and the install folder.</summary>
    /// <param name="steamPath">The <c>SteamPath</c> registry value, or null when Steam is not installed.</param>
    public static WallpaperEngineLocation Locate(WallpaperEngineConfig config, string? steamPath, ILog log)
    {
        var libraries = SteamLibraries(steamPath, log);

        var workshop = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.WorkshopPath))
        {
            var configured = Normalize(config.WorkshopPath);
            if (configured is not null && Directory.Exists(configured))
            {
                workshop.Add(configured);
            }
            else
            {
                log.Warn($"Config: 'wallpaperEngine.workshopPath' {config.WorkshopPath} does not exist; searching the Steam libraries");
            }
        }

        foreach (var library in libraries)
        {
            var folder = Path.Combine(library, "steamapps", "workshop", "content", AppId);
            if (Directory.Exists(folder))
            {
                workshop.Add(folder);
            }
        }

        string? install = null;
        if (!string.IsNullOrWhiteSpace(config.InstallPath))
        {
            var configured = Normalize(config.InstallPath);
            if (configured is not null && File.Exists(Path.Combine(configured, WallpaperExeName)))
            {
                install = configured;
            }
            else if (configured is not null && configured.EndsWith(WallpaperExeName, StringComparison.OrdinalIgnoreCase) && File.Exists(configured))
            {
                install = Path.GetDirectoryName(configured);
            }
            else
            {
                log.Warn($"Config: 'wallpaperEngine.installPath' {config.InstallPath} has no {WallpaperExeName}; searching the Steam libraries");
            }
        }

        // A Workshop folder at <library>\steamapps\workshop\content\431960 points at its library; try those first.
        var candidates = workshop.Select(LibraryOfWorkshopFolder).OfType<string>().Concat(libraries);
        install ??= candidates
            .Select(library => Path.Combine(library, "steamapps", "common", "wallpaper_engine"))
            .FirstOrDefault(folder => File.Exists(Path.Combine(folder, WallpaperExeName)));

        return new WallpaperEngineLocation(workshop.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), install);
    }

    /// <summary>The Steam library a Workshop content folder belongs to, or null.</summary>
    public static string? LibraryOfWorkshopFolder(string workshopFolder)
    {
        var full = workshopFolder.TrimEnd('\\', '/');
        var marker = full.LastIndexOf(@"\steamapps\workshop\", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? full[..marker] : null;
    }

    private static string? Normalize(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).Replace('/', '\\');
            var full = Path.GetFullPath(expanded);
            return full.Length > 3 ? full.TrimEnd('\\') : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

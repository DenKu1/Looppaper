using System.Text.Json;
using Looppaper.Core.Logging;
using Looppaper.Core.Media;

namespace Looppaper.Core.Configuration;

/// <summary>The old (standard layout) locations a portable install can import from.</summary>
public sealed record StandardLocations(string ConfigFile, string CacheDirectory, string CompressionStateFile, string CollectionFolder)
{
    public static StandardLocations Current() => new(
        AppPaths.Standard.ConfigFile,
        AppPaths.Standard.CacheDirectory,
        AppPaths.Standard.CompressionStateFile,
        AppPaths.ExpandPath(AppPaths.Standard.CollectionFolder));
}

/// <summary>
/// First run of the portable layout: copies the config, the cache, the compression records and the
/// pictures/videos of the old default collection folder into the install folder, and points paths
/// that referred to the old locations at the copies. Nothing is moved or deleted, so the old
/// locations stay usable. Runs only when the install folder has no config.json yet.
/// </summary>
public static class PortableMigration
{
    /// <summary>Returns human-readable lines describing what was copied (empty when nothing was done).</summary>
    public static IReadOnlyList<string> Run(string portableRoot, StandardLocations old, ILog log)
    {
        var done = new List<string>();
        var newConfigFile = Path.Combine(portableRoot, "config.json");
        if (File.Exists(newConfigFile) || !File.Exists(old.ConfigFile))
        {
            return done;
        }

        var newCollection = Path.Combine(portableRoot, AppPaths.PortableCollectionFolder);
        var newCache = Path.Combine(portableRoot, "cache");
        var renames = new List<(string From, string To)> { (Normalize(old.CacheDirectory), newCache) };

        var config = ConfigLoader.Parse(ReadText(old.ConfigFile) ?? string.Empty, log);
        if (SamePath(config.ResolveCollectionFolderFrom(old), old.CollectionFolder))
        {
            // The old default collection: bring its media along and use the folder inside the install.
            var copied = CopyMedia(old.CollectionFolder, newCollection, log);
            config.CollectionFolder = AppPaths.PortableCollectionFolder;
            renames.Add((Normalize(old.CollectionFolder), newCollection));
            if (copied > 0)
            {
                done.Add($"Copied {copied} picture(s)/video(s) from {old.CollectionFolder} to {newCollection}");
            }
        }

        config.Video.File = Rename(config.Video.File, renames);
        config.Picture = Rename(config.Picture, renames);
        if (ConfigLoader.TrySave(newConfigFile, config, log))
        {
            done.Add($"Copied settings from {old.ConfigFile} to {newConfigFile}");
        }

        var cached = CopyFiles(old.CacheDirectory, newCache, log);
        if (cached > 0)
        {
            done.Add($"Copied {cached} cached video(s) from {old.CacheDirectory} to {newCache}");
        }

        if (File.Exists(old.CompressionStateFile) && ReadText(old.CompressionStateFile) is { } state)
        {
            var newState = Path.Combine(portableRoot, "compression.json");
            if (!File.Exists(newState) && TryWrite(newState, RenameInJson(state, renames), log))
            {
                done.Add($"Copied compression records to {newState}");
            }
        }

        foreach (var line in done)
        {
            log.Info($"Migration: {line}");
        }

        return done;
    }

    private static string ResolveCollectionFolderFrom(this LooppaperConfig config, StandardLocations old)
    {
        // The old config was written for the standard layout, where the default is an absolute %USERPROFILE% path.
        try
        {
            return AppPaths.ExpandPath(config.CollectionFolder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return old.CollectionFolder;
        }
    }

    /// <summary>Points a path inside one of the old folders at the same file in the new folder.</summary>
    public static string? Rename(string? path, IReadOnlyList<(string From, string To)> renames)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        foreach (var (from, to) in renames)
        {
            var prefix = from + Path.DirectorySeparatorChar;
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(to, path[prefix.Length..]);
            }
        }

        return path;
    }

    /// <summary>Rewrites old folder paths inside a JSON document (string values are JSON-escaped).</summary>
    public static string RenameInJson(string json, IReadOnlyList<(string From, string To)> renames)
    {
        foreach (var (from, to) in renames)
        {
            var escapedFrom = JsonSerializer.Serialize(from + Path.DirectorySeparatorChar).Trim('"');
            var escapedTo = JsonSerializer.Serialize(to + Path.DirectorySeparatorChar).Trim('"');
            json = ReplaceIgnoreCase(json, escapedFrom, escapedTo);
        }

        return json;
    }

    private static int CopyMedia(string from, string to, ILog log) =>
        CopyFiles(from, to, log, name => MediaScanner.IsSupported(name, MediaKind.Picture) || MediaScanner.IsSupported(name, MediaKind.Video));

    private static int CopyFiles(string from, string to, ILog log, Func<string, bool>? include = null)
    {
        if (!Directory.Exists(from))
        {
            return 0;
        }

        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(from))
            {
                if (include is not null && !include(file))
                {
                    continue;
                }

                var target = Path.Combine(to, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    continue;
                }

                Directory.CreateDirectory(to);
                File.Copy(file, target); // keeps the last-write time, so cached-copy records still match
                count++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Migration: copying {from} to {to} stopped early: {ex.Message}");
        }

        return count;
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryWrite(string path, string text, ILog log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Migration: writing {path} failed: {ex.Message}");
            return false;
        }
    }

    private static string ReplaceIgnoreCase(string text, string oldValue, string newValue) =>
        text.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
}

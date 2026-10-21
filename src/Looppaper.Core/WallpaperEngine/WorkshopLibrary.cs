using System.Globalization;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Media;

namespace Looppaper.Core.WallpaperEngine;

/// <summary>Whether Looppaper can play a Workshop item, and what it needs first.</summary>
public enum WorkshopItemStatus
{
    /// <summary>An <c>.mp4</c> video within the 100 MB limit: played directly.</summary>
    Ready,

    /// <summary>A video over the 100 MB limit: needs compressing first (Epic 9).</summary>
    TooLarge,

    /// <summary>A scene or web wallpaper that has not been recorded to a video yet.</summary>
    NeedsConversion,

    /// <summary>A scene or web wallpaper with an up-to-date recording in the cache.</summary>
    Converted,

    /// <summary>Application wallpapers, non-<c>.mp4</c> videos, unknown types and broken items.</summary>
    Unsupported,
}

/// <summary>One wallpaper in the local Wallpaper Engine Workshop folder.</summary>
/// <param name="Number">1-based position in the sorted list, used by <c>--we-set</c>.</param>
/// <param name="Id">The Workshop id (the item's folder name).</param>
/// <param name="FilePath">Full path of the main file (video, scene.json, index.html...), or null.</param>
/// <param name="SizeBytes">The video file's size for videos, otherwise the size of the whole folder.</param>
/// <param name="ModifiedUtc">Newest write time of the item's files; part of the conversion cache key.</param>
/// <param name="ConvertedFile">The cached recording of a scene / web item, when there is one.</param>
/// <param name="Note">Why an item is unsupported, or null.</param>
public sealed record WorkshopItem(
    int Number,
    string Id,
    string Folder,
    string Title,
    WorkshopItemType Type,
    string? FilePath,
    long SizeBytes,
    DateTime ModifiedUtc,
    WorkshopItemStatus Status,
    string? ConvertedFile,
    string? Note)
{
    public string ProjectFilePath => Path.Combine(Folder, ProjectFile.FileName);

    /// <summary>The <c>.mp4</c> to play right now, or null when the item is not playable as it is.</summary>
    public string? PlayableFile => Status switch
    {
        WorkshopItemStatus.Ready => FilePath,
        WorkshopItemStatus.Converted => ConvertedFile,
        _ => null,
    };
}

/// <summary>
/// Reads every item in the Workshop folders and decides its status. The list is sorted by type
/// (video, scene, web, application, other), then title, then id, so numbers stay the same as long
/// as the set of downloaded items does not change.
/// </summary>
public static class WorkshopLibrary
{
    public static IReadOnlyList<WorkshopItem> Scan(IEnumerable<string> workshopFolders, string cacheDirectory, ILog log)
    {
        var maxBytes = (long)VideoConfig.MaxFileSizeCapMb * MediaScanner.BytesPerMb;
        var items = new List<WorkshopItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in workshopFolders)
        {
            IEnumerable<string> folders;
            try
            {
                folders = Directory.EnumerateDirectories(root).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warn($"Cannot read Workshop folder {root}: {ex.Message}");
                continue;
            }

            foreach (var folder in folders)
            {
                var id = Path.GetFileName(folder);
                if (!seen.Add(id))
                {
                    continue;
                }

                var project = ProjectFile.Read(folder);
                if (project is null)
                {
                    if (File.Exists(Path.Combine(folder, ProjectFile.FileName)))
                    {
                        log.Warn($"Workshop item {id}: project.json could not be read");
                        items.Add(Unreadable(id, folder));
                    }

                    continue;
                }

                items.Add(Describe(id, folder, project, cacheDirectory, maxBytes));
            }
        }

        return items
            .OrderBy(i => i.Type)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select((item, index) => item with { Number = index + 1 })
            .ToList();
    }

    /// <summary>Builds the item for one folder; public for tests.</summary>
    public static WorkshopItem Describe(string id, string folder, ProjectInfo project, string cacheDirectory, long maxVideoBytes)
    {
        var title = project.Title ?? id;
        var type = project.Type;
        var file = project.File is null ? null : SafeCombine(folder, project.File);
        var modified = ConversionCache.ModifiedUtc(folder);

        switch (type)
        {
            case WorkshopItemType.Video:
            {
                if (file is null || !File.Exists(file))
                {
                    return new(0, id, folder, title, type, file, FolderSize(folder), modified, WorkshopItemStatus.Unsupported, null, "video file is missing");
                }

                var size = new FileInfo(file).Length;
                if (!MediaScanner.IsSupported(file, MediaKind.Video))
                {
                    return new(0, id, folder, title, type, file, size, modified, WorkshopItemStatus.Unsupported, null, $"{Path.GetExtension(file)} video; only .mp4 is supported");
                }

                var status = size > maxVideoBytes ? WorkshopItemStatus.TooLarge : WorkshopItemStatus.Ready;
                return new(0, id, folder, title, type, file, size, modified, status, null, null);
            }

            case WorkshopItemType.Scene:
            case WorkshopItemType.Web:
            {
                var converted = ConversionCache.Find(cacheDirectory, id, modified);
                var status = converted is null ? WorkshopItemStatus.NeedsConversion : WorkshopItemStatus.Converted;
                return new(0, id, folder, title, type, file, FolderSize(folder), modified, status, converted, null);
            }

            case WorkshopItemType.Application:
                return new(0, id, folder, title, type, file, FolderSize(folder), modified, WorkshopItemStatus.Unsupported, null, "application wallpapers are programs and cannot be played");

            default:
                return new(0, id, folder, title, type, file, FolderSize(folder), modified, WorkshopItemStatus.Unsupported, null, $"unknown type '{project.TypeText}'");
        }
    }

    /// <summary>Finds an item by its list number or its Workshop id.</summary>
    public static WorkshopItem? Select(IReadOnlyList<WorkshopItem> items, string numberOrId)
    {
        var text = numberOrId.Trim();
        var byId = items.FirstOrDefault(i => string.Equals(i.Id, text, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= items.Count
            ? items[number - 1]
            : null;
    }

    public static string StatusText(WorkshopItemStatus status) => status switch
    {
        WorkshopItemStatus.Ready => "ready",
        WorkshopItemStatus.TooLarge => "too large",
        WorkshopItemStatus.NeedsConversion => "needs conversion",
        WorkshopItemStatus.Converted => "converted",
        _ => "unsupported",
    };

    public static string TypeText(WorkshopItemType type) => type.ToString().ToLowerInvariant();

    public static string SizeText(long bytes) =>
        bytes >= MediaScanner.BytesPerMb
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)MediaScanner.BytesPerMb:F1} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(bytes / 1024.0, bytes > 0 ? 0.1 : 0):F1} KB");

    /// <summary>The console table printed by <c>--we-list</c>.</summary>
    public static IEnumerable<string> FormatTable(IReadOnlyList<WorkshopItem> items)
    {
        var rows = items.Select(i => new[]
        {
            i.Number.ToString(CultureInfo.InvariantCulture),
            i.Id,
            TypeText(i.Type),
            SizeText(i.SizeBytes),
            StatusText(i.Status),
            i.Title,
        }).ToList();
        var header = new[] { "#", "Workshop id", "Type", "Size", "Status", "Title" };
        var widths = Enumerable.Range(0, header.Length - 1)
            .Select(c => rows.Select(r => r[c].Length).Append(header[c].Length).Max())
            .ToArray();

        string Line(string[] cells)
        {
            var parts = new List<string>
            {
                cells[0].PadLeft(widths[0]),
                cells[1].PadRight(widths[1]),
                cells[2].PadRight(widths[2]),
                cells[3].PadLeft(widths[3]),
                cells[4].PadRight(widths[4]),
                cells[5],
            };
            return string.Join("  ", parts).TrimEnd();
        }

        yield return Line(header);
        foreach (var row in rows)
        {
            yield return Line(row);
        }
    }

    private static WorkshopItem Unreadable(string id, string folder) =>
        new(0, id, folder, id, WorkshopItemType.Unknown, null, FolderSize(folder), ConversionCache.ModifiedUtc(folder), WorkshopItemStatus.Unsupported, null, "project.json could not be read");

    /// <summary>Joins a project-relative path, refusing paths that escape the item folder.</summary>
    private static string? SafeCombine(string folder, string relative)
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(folder, relative));
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static long FolderSize(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}

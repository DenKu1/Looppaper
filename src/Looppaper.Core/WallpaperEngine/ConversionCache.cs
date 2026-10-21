using System.Globalization;

namespace Looppaper.Core.WallpaperEngine;

/// <summary>
/// Names of converted Workshop items in the cache folder (<see cref="AppPaths.CacheDirectory"/>):
/// <c>we-&lt;workshop id&gt;-&lt;modified time&gt;.mp4</c>. When the Workshop item is updated its
/// modified time changes, so an old conversion is no longer found and is converted again.
/// </summary>
public static class ConversionCache
{
    private const string Prefix = "we-";
    private const string StampFormat = "yyyyMMdd'T'HHmmss";

    /// <summary>The cache key for an item: its id plus the newest write time of its top-level files.</summary>
    public static string FileName(string workshopId, DateTime modifiedUtc) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{Sanitize(workshopId)}-{modifiedUtc.ToUniversalTime().ToString(StampFormat, CultureInfo.InvariantCulture)}.mp4");

    public static string PathFor(string cacheDirectory, string workshopId, DateTime modifiedUtc) =>
        Path.Combine(cacheDirectory, FileName(workshopId, modifiedUtc));

    /// <summary>The cached conversion for this exact version of the item, or null.</summary>
    public static string? Find(string cacheDirectory, string workshopId, DateTime modifiedUtc)
    {
        var path = PathFor(cacheDirectory, workshopId, modifiedUtc);
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Older conversions of the same item (other modified times), which can be deleted.</summary>
    public static IEnumerable<string> Stale(string cacheDirectory, string workshopId, DateTime modifiedUtc)
    {
        if (!Directory.Exists(cacheDirectory))
        {
            return [];
        }

        var current = FileName(workshopId, modifiedUtc);
        try
        {
            return Directory.EnumerateFiles(cacheDirectory, $"{Prefix}{Sanitize(workshopId)}-*.mp4")
                .Where(f => !string.Equals(Path.GetFileName(f), current, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Newest write time (UTC) of the files directly in the item folder: changes when Steam updates the item.</summary>
    public static DateTime ModifiedUtc(string itemFolder)
    {
        try
        {
            var newest = Directory.EnumerateFiles(itemFolder)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(itemFolder))
                .Max();
            return DateTime.SpecifyKind(newest, DateTimeKind.Utc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static string Sanitize(string id) =>
        new(id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}

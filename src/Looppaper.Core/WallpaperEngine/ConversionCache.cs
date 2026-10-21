using System.Globalization;

namespace Looppaper.Core.WallpaperEngine;

/// <summary>
/// Names of videos made from Workshop items (scene / web recordings and copies of video wallpapers):
/// <c>&lt;title&gt; (we-&lt;workshop id&gt;-&lt;modified time&gt;).mp4</c> in the video folder. When the Workshop item is
/// updated its modified time changes, so an old video is no longer found and is made again. Older versions of
/// Looppaper saved them as <c>we-&lt;id&gt;-&lt;time&gt;.mp4</c> in the cache folder; those are still found by their tag.
/// </summary>
public static class ConversionCache
{
    private const string Prefix = "we-";
    private const string StampFormat = "yyyyMMdd'T'HHmmss";
    private const int StampLength = 15;
    private const int MaxTitleLength = 80;

    /// <summary>The key for an item: <c>we-&lt;id&gt;-&lt;newest write time of its top-level files&gt;</c>.</summary>
    public static string Tag(string workshopId, DateTime modifiedUtc) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{Sanitize(workshopId)}-{modifiedUtc.ToUniversalTime().ToString(StampFormat, CultureInfo.InvariantCulture)}");

    /// <summary><c>&lt;title&gt; (&lt;tag&gt;).mp4</c>, with characters Windows does not allow in file names left out.</summary>
    public static string FileName(string title, string workshopId, DateTime modifiedUtc) =>
        FileName(title, Tag(workshopId, modifiedUtc));

    /// <summary><c>&lt;title&gt; (&lt;tag&gt;).mp4</c>, or <c>&lt;tag&gt;.mp4</c> when there is no usable title.</summary>
    public static string FileName(string title, string tag)
    {
        var name = SafeTitle(title);
        return name.Length == 0 ? tag + ".mp4" : $"{name} ({tag}).mp4";
    }

    public static string PathFor(string folder, string title, string workshopId, DateTime modifiedUtc) =>
        Path.Combine(folder, FileName(title, workshopId, modifiedUtc));

    /// <summary>The video for this exact version of the item in any of <paramref name="folders"/>, or null.</summary>
    public static string? Find(IEnumerable<string> folders, string workshopId, DateTime modifiedUtc)
    {
        var tag = Tag(workshopId, modifiedUtc);
        foreach (var folder in folders)
        {
            foreach (var file in Matching(folder, tag).Where(f => string.Equals(TagOf(f), tag, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (new FileInfo(file).Length > 0)
                    {
                        return file;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return null;
    }

    /// <summary>Videos of older versions of the same item (other modified times), which can be deleted.</summary>
    public static IEnumerable<string> Stale(IEnumerable<string> folders, string workshopId, DateTime modifiedUtc)
    {
        var current = Tag(workshopId, modifiedUtc);
        var prefix = $"{Prefix}{Sanitize(workshopId)}-";
        return folders
            .SelectMany(folder => Matching(folder, prefix))
            .Where(f => TagOf(f) is { } tag && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(tag, current, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Recordings in the old naming (<c>we-&lt;id&gt;-&lt;time&gt;.mp4</c>) directly in <paramref name="cacheDirectory"/>.</summary>
    public static IReadOnlyList<string> Legacy(string cacheDirectory)
    {
        try
        {
            return Directory.Exists(cacheDirectory)
                ? Directory.EnumerateFiles(cacheDirectory, $"{Prefix}*.mp4").Where(f => TagOf(f) is not null).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The <c>we-&lt;id&gt;-&lt;time&gt;</c> tag in a file name in either naming, or null.</summary>
    public static string? TagOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(')') && name.LastIndexOf(" (" + Prefix, StringComparison.OrdinalIgnoreCase) is var open and >= 0)
        {
            name = name[(open + 2)..^1];
        }

        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && name.Length > Prefix.Length + StampLength
            && DateTime.TryParseExact(name[^StampLength..], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? name
            : null;
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

    /// <summary>A Workshop title as a file name: no characters Windows forbids, no trailing dots or spaces, at most 80 characters.</summary>
    public static string SafeTitle(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > MaxTitleLength)
        {
            cleaned = cleaned[..MaxTitleLength];
        }

        return cleaned.TrimEnd('.', ' ');
    }

    /// <summary>Files in <paramref name="folder"/> whose name contains <paramref name="text"/> and ends in .mp4.</summary>
    private static IEnumerable<string> Matching(string folder, string text)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, $"*{text}*.mp4").ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static string Sanitize(string id) =>
        new(id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}

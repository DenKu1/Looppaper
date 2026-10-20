using Looppaper.Core.Logging;

namespace Looppaper.Core.Media;

public enum MediaKind
{
    Picture,
    Music,
}

/// <summary>
/// Lists supported files in the top level of a folder. Extensions are matched case-insensitively,
/// oversize and hidden files are skipped, and results are sorted ordinally (ignoring case) so the
/// order is stable across runs. Each skipped file is logged once per scanner instance, so rescans
/// do not flood the log.
/// </summary>
public sealed class MediaScanner
{
    public static readonly IReadOnlyList<string> PictureExtensions = [".jpg", ".jpeg", ".png"];
    public static readonly IReadOnlyList<string> MusicExtensions = [".mp3"];

    public const long BytesPerMb = 1024 * 1024;

    private readonly ILog _log;
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingFolders = new(StringComparer.OrdinalIgnoreCase);

    public MediaScanner(ILog log)
    {
        _log = log;
    }

    public static IReadOnlyList<string> ExtensionsFor(MediaKind kind) =>
        kind == MediaKind.Picture ? PictureExtensions : MusicExtensions;

    public static bool IsSupported(string path, MediaKind kind)
    {
        var ext = Path.GetExtension(path);
        return ExtensionsFor(kind).Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns full paths of supported files no larger than <paramref name="maxSizeMb"/>.</summary>
    public IReadOnlyList<string> Scan(string folder, MediaKind kind, int maxSizeMb)
    {
        var maxBytes = maxSizeMb * BytesPerMb;
        var label = kind == MediaKind.Picture ? "picture" : "music file";

        IEnumerable<FileInfo> files;
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists)
            {
                if (_missingFolders.Add(folder))
                {
                    _log.Warn($"Folder {folder} does not exist; no {label}s to show");
                }

                return [];
            }

            _missingFolders.Remove(folder);
            files = dir.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                MatchCasing = MatchCasing.CaseInsensitive,
            }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Error($"Cannot list {folder}", ex);
            return [];
        }

        var result = new List<string>();
        foreach (var file in files)
        {
            if (!IsSupported(file.Name, kind))
            {
                continue;
            }

            if (file.Length > maxBytes)
            {
                if (_reported.Add(file.FullName))
                {
                    _log.Warn($"Skipping {label} {file.FullName}: {file.Length / (double)BytesPerMb:F1} MB is above the {maxSizeMb} MB limit");
                }

                continue;
            }

            if (file.Length == 0)
            {
                if (_reported.Add(file.FullName))
                {
                    _log.Warn($"Skipping empty {label} {file.FullName}");
                }

                continue;
            }

            result.Add(file.FullName);
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Looppaper.Core.Media;

namespace Looppaper.Core.Compression;

/// <summary>
/// Identifies one version of a video file: its full path, size and last-write time. When the file is
/// replaced or edited the key changes, so a remembered compressed copy no longer applies.
/// </summary>
public sealed record VideoFileKey(string FullPath, long SizeBytes, long LastWriteUtcTicks)
{
    /// <summary>Reads the key of an existing file; null when it does not exist or cannot be read.</summary>
    public static VideoFileKey? TryFromFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new VideoFileKey(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public double SizeMb => SizeBytes / (double)MediaScanner.BytesPerMb;

    public string FileName => Path.GetFileName(FullPath);

    /// <summary>True when this key describes the same file version (paths compare case-insensitively).</summary>
    public bool Matches(VideoFileKey other) =>
        string.Equals(FullPath, other.FullPath, StringComparison.OrdinalIgnoreCase)
        && SizeBytes == other.SizeBytes
        && LastWriteUtcTicks == other.LastWriteUtcTicks;

    /// <summary>Short stable hash of path, size and time; used in cache file names.</summary>
    public string ShortHash()
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{FullPath.ToUpperInvariant()}|{SizeBytes}|{LastWriteUtcTicks}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 5).ToLowerInvariant();
    }
}

/// <summary>
/// Video sizes. There is no hard limit: every video plays, but one that is still over
/// <see cref="WarningMb"/> after compression gets a warning that it can slow the PC down.
/// </summary>
public static class VideoSize
{
    public const int WarningMb = 100;

    public static long WarningBytes => WarningMb * MediaScanner.BytesPerMb;

    public static bool IsLarge(long sizeBytes) => sizeBytes > WarningBytes;

    /// <summary>"218.3 MB" with an invariant decimal point.</summary>
    public static string FormatMb(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)MediaScanner.BytesPerMb:F1} MB");
}

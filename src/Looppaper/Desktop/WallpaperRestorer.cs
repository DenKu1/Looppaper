using Looppaper.Core.Logging;
using Looppaper.Native;
using System.Runtime.InteropServices;

namespace Looppaper.Desktop;

/// <summary>
/// Remembers the user's wallpaper at start-up and re-applies it on exit, which makes Explorer
/// repaint the desktop so no stale frame of ours is left behind.
/// </summary>
internal static unsafe class WallpaperRestorer
{
    private const int BufferChars = 1024;

    private static string? _originalPath;
    private static int _restored;

    public static string? OriginalPath => _originalPath;

    public static void Capture(ILog log)
    {
        var buffer = stackalloc char[BufferChars];
        if (User32.SystemParametersInfo(User32.SPI_GETDESKWALLPAPER, BufferChars, buffer, 0))
        {
            _originalPath = new string(buffer);
            log.Info($"Current Windows wallpaper: {(_originalPath.Length == 0 ? "(none / solid color)" : _originalPath)}");
        }
        else
        {
            log.Warn($"SPI_GETDESKWALLPAPER failed ({Marshal.GetLastPInvokeError()})");
        }
    }

    /// <summary>Re-applies the original wallpaper. Runs at most once per process.</summary>
    public static void Restore(ILog log)
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0 || _originalPath is null)
        {
            return;
        }

        Apply(log);
    }

    /// <summary>Re-applies the original wallpaper every time it is called (the watchdog may need it more than once).</summary>
    public static void Reapply(ILog log)
    {
        if (_originalPath is not null)
        {
            Apply(log);
        }
    }

    private static void Apply(ILog log)
    {
        fixed (char* path = _originalPath)
        {
            if (User32.SystemParametersInfo(User32.SPI_SETDESKWALLPAPER, 0, path, User32.SPIF_UPDATEINIFILE))
            {
                log.Info("Original wallpaper restored");
            }
            else
            {
                log.Warn($"Restoring the wallpaper failed ({Marshal.GetLastPInvokeError()})");
            }
        }
    }
}

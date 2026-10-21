using Looppaper.Core.Logging;
using Looppaper.Native;
using static Looppaper.Native.Shell32;

namespace Looppaper.Desktop;

/// <summary>
/// Non-blocking Windows notifications for the background wallpaper process (no console to print to).
/// Uses a temporary notification-area icon with a balloon, which Windows 10/11 shows as a regular
/// notification; the icon is removed again after <see cref="ShowFor"/>. The permanent tray icon
/// (<see cref="TrayIcon"/>, off by default) is a separate icon.
/// </summary>
internal static unsafe class TrayNotification
{
    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(20);
    private static int _nextId = 100;

    /// <summary>Shows a notification owned by <paramref name="hwnd"/> (a window of this process). Never throws.</summary>
    public static void Show(IntPtr hwnd, string title, string text, bool warning, ILog log)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = hwnd,
            uID = (uint)Interlocked.Increment(ref _nextId),
            uFlags = NIF_ICON | NIF_TIP | NIF_INFO,
            hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION),
            dwInfoFlags = warning ? NIIF_WARNING : NIIF_INFO,
        };
        CopyText(title, data.szTip, 128);
        CopyText(text, data.szInfo, 256);
        CopyText(title, data.szInfoTitle, 64);

        if (!Shell_NotifyIcon(NIM_ADD, ref data))
        {
            log.Warn($"Showing a Windows notification failed; it said: {text}");
            return;
        }

        var remove = new NOTIFYICONDATAW { cbSize = data.cbSize, hWnd = hwnd, uID = data.uID };
        _ = Task.Delay(ShowFor).ContinueWith(_ => Shell_NotifyIcon(NIM_DELETE, ref remove), TaskScheduler.Default);
    }
}

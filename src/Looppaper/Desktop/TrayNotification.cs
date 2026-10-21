using System.Runtime.InteropServices;
using Looppaper.Core.Logging;

namespace Looppaper.Desktop;

/// <summary>
/// Non-blocking Windows notifications for the background wallpaper process (no console to print to).
/// Uses a temporary notification-area icon with a balloon, which Windows 10/11 shows as a regular
/// notification; the icon is removed again after <see cref="ShowFor"/>. Looppaper has no permanent tray icon.
/// </summary>
internal static unsafe partial class TrayNotification
{
    private const uint NIM_ADD = 0x0;
    private const uint NIM_DELETE = 0x2;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;
    private const uint NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x1;
    private const uint NIIF_WARNING = 0x2;
    private const int IDI_APPLICATION = 32512;

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
        Copy(title, data.szTip, 128);
        Copy(text, data.szInfo, 256);
        Copy(title, data.szInfoTitle, 64);

        if (!Shell_NotifyIcon(NIM_ADD, ref data))
        {
            log.Warn($"Showing a Windows notification failed; it said: {text}");
            return;
        }

        var remove = new NOTIFYICONDATAW { cbSize = data.cbSize, hWnd = hwnd, uID = data.uID };
        _ = Task.Delay(ShowFor).ContinueWith(_ => Shell_NotifyIcon(NIM_DELETE, ref remove), TaskScheduler.Default);
    }

    private static void Copy(string text, char* target, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        for (var i = 0; i < length; i++)
        {
            target[i] = text[i];
        }

        if (text.Length >= capacity && length > 0)
        {
            target[length - 1] = '…';
        }

        target[length] = '\0';
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATAW data);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial IntPtr LoadIcon(IntPtr instance, IntPtr iconName);
}

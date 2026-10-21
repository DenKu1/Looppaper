using Looppaper.Core.Playback;
using Looppaper.Core.Rendering;
using Looppaper.Native;

namespace Looppaper.Desktop;

/// <summary>Finds full-screen apps and games that hide the wallpaper. Cheap enough to poll every few seconds.</summary>
internal static unsafe class FullscreenDetector
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>
    /// The monitor (relative to the virtual screen) the foreground window covers completely, if any, and whether
    /// Windows reports an exclusive full-screen Direct3D app or presentation mode.
    /// </summary>
    public static (IntRect? Covered, bool Exclusive) Check()
    {
        var exclusive = Shell32.SHQueryUserNotificationState(out var state) >= 0 &&
            state is Shell32.QUNS_RUNNING_D3D_FULL_SCREEN or Shell32.QUNS_PRESENTATION_MODE;
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsIgnored(hwnd) || !User32.GetWindowRect(hwnd, out var window))
        {
            return (null, exclusive);
        }

        var monitor = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONULL);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor == IntPtr.Zero || !User32.GetMonitorInfo(monitor, ref info))
        {
            return (null, exclusive);
        }

        var origin = DesktopHost.VirtualScreen();
        var monitorRect = Relative(info.rcMonitor, origin);
        return (FullscreenRules.Covers(Relative(window, origin), monitorRect) ? monitorRect : null, exclusive);
    }

    /// <summary>The desktop, the taskbar, our own windows and hidden or minimized windows never count.</summary>
    private static bool IsIgnored(IntPtr hwnd)
    {
        if (hwnd == User32.GetShellWindow() || !User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd))
        {
            return true;
        }

        User32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return true;
        }

        var buffer = stackalloc char[64];
        var length = User32.GetClassName(hwnd, buffer, 64);
        var className = new string(buffer, 0, Math.Max(length, 0));
        return ShellClasses.Contains(className, StringComparer.Ordinal);
    }

    private static IntRect Relative(in RECT rect, in RECT origin) =>
        new(rect.Left - origin.Left, rect.Top - origin.Top, rect.Width, rect.Height);
}

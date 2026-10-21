using Looppaper.Core.Logging;
using Looppaper.Native;
using System.Runtime.InteropServices;

namespace Looppaper.Desktop;

/// <summary>Receives the wallpaper window's messages on the UI thread.</summary>
internal interface IWallpaperWindowHandler
{
    void OnPaint(IntPtr hdc, in RECT area);

    /// <summary>
    /// The window was destroyed without <see cref="WallpaperWindow.Destroy"/>, i.e. by Explorer going away.
    /// Called from inside WM_DESTROY, so it should only schedule work.
    /// </summary>
    void OnWindowLost();
}

/// <summary>
/// The raw Win32 child window drawn behind the desktop icons. At most one exists at a time; it covers the
/// whole virtual screen and does no work beyond what its handler does in WM_PAINT. It dies with Explorer,
/// so it is recreated in the new desktop when Explorer restarts (see <see cref="ControlWindow"/>).
/// </summary>
internal sealed unsafe class WallpaperWindow
{
    private const string ClassName = "LooppaperWallpaperWindow";

    private static WallpaperWindow? _instance;
    private static bool _classRegistered;

    private readonly IWallpaperWindowHandler _handler;
    private readonly ILog _log;
    private bool _destroying;

    private WallpaperWindow(IWallpaperWindowHandler handler, ILog log)
    {
        _handler = handler;
        _log = log;
    }

    public IntPtr Handle { get; private set; }

    /// <summary>True while the window exists (it is destroyed together with Explorer's desktop).</summary>
    public bool IsAlive => Handle != IntPtr.Zero && User32.IsWindow(Handle);

    public static WallpaperWindow? Create(DesktopHost host, IWallpaperWindowHandler handler, ILog log)
    {
        if (_instance is not null)
        {
            throw new InvalidOperationException("Only one wallpaper window at a time.");
        }

        var hInstance = Kernel32.GetModuleHandle(null);
        if (!_classRegistered)
        {
            fixed (char* name = ClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = hInstance,
                    lpszClassName = name,
                };
                if (User32.RegisterClassEx(wc) == 0)
                {
                    log.Error($"RegisterClassEx failed ({Marshal.GetLastPInvokeError()})");
                    return null;
                }
            }

            _classRegistered = true;
        }

        var window = new WallpaperWindow(handler, log);
        _instance = window;

        var bounds = host.ChildBounds();
        var hwnd = User32.CreateWindowEx(
            host.ExtendedStyle | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW,
            ClassName,
            "Looppaper",
            User32.WS_CHILD | User32.WS_CLIPSIBLINGS | User32.WS_CLIPCHILDREN,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            host.Parent,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            log.Error($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()})");
            _instance = null;
            return null;
        }

        window.Handle = hwnd;
        host.Attach(hwnd, log);
        User32.ShowWindow(hwnd, User32.SW_SHOWNA);
        log.Info($"Wallpaper window 0x{hwnd:X} created at {bounds} in parent 0x{host.Parent:X}; virtual screen {DesktopHost.VirtualScreen()}");
        return window;
    }

    /// <summary>Destroys the window on purpose (shutdown or recreating it); the handler is not told.</summary>
    public void Destroy()
    {
        _destroying = true;
        if (Handle != IntPtr.Zero)
        {
            User32.DestroyWindow(Handle);
        }

        Handle = IntPtr.Zero;
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    public void Invalidate()
    {
        if (Handle != IntPtr.Zero)
        {
            User32.InvalidateRect(Handle, IntPtr.Zero, false);
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _instance;
        if (self is null || self.Handle != hwnd)
        {
            return User32.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        try
        {
            return self.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // An exception must not unwind into native code; log it and carry on.
            self._log.Error($"Unhandled exception in wallpaper window message 0x{msg:X}", ex);
            return IntPtr.Zero;
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case User32.WM_ERASEBKGND:
                return 1;
            case User32.WM_PAINT:
                var hdc = User32.BeginPaint(hwnd, out var ps);
                try
                {
                    _handler.OnPaint(hdc, ps.rcPaint);
                }
                finally
                {
                    User32.EndPaint(hwnd, ps);
                }

                return IntPtr.Zero;
            case User32.WM_DESTROY:
                Handle = IntPtr.Zero;
                _instance = null;
                if (!_destroying)
                {
                    _log.Warn($"Wallpaper window 0x{hwnd:X} was destroyed from outside (Explorer closed or restarted?)");
                    _handler.OnWindowLost();
                }

                return IntPtr.Zero;
        }

        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}

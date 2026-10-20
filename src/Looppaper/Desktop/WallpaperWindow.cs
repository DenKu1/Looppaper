using Looppaper.Core.Logging;
using Looppaper.Native;
using System.Runtime.InteropServices;

namespace Looppaper.Desktop;

/// <summary>Receives the wallpaper window's messages on the UI thread.</summary>
internal interface IWallpaperWindowHandler
{
    void OnPaint(IntPtr hdc, in RECT area);

    /// <summary>Called for messages the window does not handle itself. Return true when handled.</summary>
    bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Windows is logging off or shutting down.</summary>
    void OnEndSession();
}

/// <summary>
/// The raw Win32 child window drawn behind the desktop icons. One per process; it covers the
/// whole virtual screen and does no work beyond what its handler does in WM_PAINT.
/// </summary>
internal sealed unsafe class WallpaperWindow
{
    private const string ClassName = "LooppaperWallpaperWindow";

    private static WallpaperWindow? _instance;
    private static bool _classRegistered;

    private readonly IWallpaperWindowHandler _handler;
    private readonly ILog _log;

    private WallpaperWindow(IWallpaperWindowHandler handler, ILog log)
    {
        _handler = handler;
        _log = log;
    }

    public IntPtr Handle { get; private set; }

    public static WallpaperWindow? Create(DesktopHost host, IWallpaperWindowHandler handler, ILog log)
    {
        if (_instance is not null)
        {
            throw new InvalidOperationException("Only one wallpaper window per process.");
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

    /// <summary>Asks the window to close from any thread.</summary>
    public void RequestClose()
    {
        if (Handle != IntPtr.Zero)
        {
            User32.PostMessage(Handle, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void Invalidate()
    {
        if (Handle != IntPtr.Zero)
        {
            User32.InvalidateRect(Handle, IntPtr.Zero, false);
        }
    }

    /// <summary>Pumps messages until the window is destroyed.</summary>
    public static void RunMessageLoop()
    {
        while (User32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            User32.TranslateMessage(msg);
            User32.DispatchMessage(msg);
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _instance;
        if (self is null)
        {
            return User32.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        try
        {
            return self.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // An exception must not unwind into native code; log it and shut down cleanly instead.
            self._log.Error($"Unhandled exception in window message 0x{msg:X}; shutting down", ex);
            User32.PostMessage(hwnd, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
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
            case User32.WM_CLOSE:
                User32.DestroyWindow(hwnd);
                return IntPtr.Zero;
            case User32.WM_DESTROY:
                Handle = IntPtr.Zero;
                User32.PostQuitMessage(0);
                return IntPtr.Zero;
            case User32.WM_QUERYENDSESSION:
                return 1;
            case User32.WM_ENDSESSION:
                if (wParam != IntPtr.Zero)
                {
                    _handler.OnEndSession();
                }

                return IntPtr.Zero;
        }

        return _handler.OnMessage(msg, wParam, lParam) ? IntPtr.Zero : User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}

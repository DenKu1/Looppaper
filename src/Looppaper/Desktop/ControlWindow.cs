using Looppaper.Core.Logging;
using Looppaper.Native;
using System.Runtime.InteropServices;

namespace Looppaper.Desktop;

/// <summary>Receives the control window's messages on the UI thread.</summary>
internal interface IControlWindowHandler
{
    /// <summary>Called for messages the window does not handle itself. Return true when handled.</summary>
    bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Explorer (re)created the taskbar, e.g. after it was restarted.</summary>
    void OnTaskbarCreated();

    /// <summary>Windows is logging off or shutting down.</summary>
    void OnEndSession();
}

/// <summary>
/// A hidden top-level window that lives as long as the wallpaper process. Unlike the wallpaper window
/// (a child of Explorer's desktop, gone when Explorer restarts) it keeps its handle, so timers, worker
/// threads and Media Engine callbacks post here, and it receives the broadcasts only top-level windows
/// get: <c>TaskbarCreated</c>, end of session, display and power changes. Closing it ends the message loop.
/// </summary>
internal sealed unsafe class ControlWindow
{
    private const string ClassName = "LooppaperControlWindow";

    private static ControlWindow? _instance;

    private readonly IControlWindowHandler _handler;
    private readonly ILog _log;
    private readonly uint _taskbarCreated;

    private ControlWindow(IControlWindowHandler handler, ILog log, uint taskbarCreated)
    {
        _handler = handler;
        _log = log;
        _taskbarCreated = taskbarCreated;
    }

    public IntPtr Handle { get; private set; }

    public static ControlWindow? Create(IControlWindowHandler handler, ILog log)
    {
        if (_instance is not null)
        {
            throw new InvalidOperationException("Only one control window per process.");
        }

        var hInstance = Kernel32.GetModuleHandle(null);
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
                log.Error($"Registering the control window class failed ({Marshal.GetLastPInvokeError()})");
                return null;
            }
        }

        var window = new ControlWindow(handler, log, User32.RegisterWindowMessage("TaskbarCreated"));
        _instance = window;

        // Top-level (not message-only: those miss broadcasts), never shown.
        var hwnd = User32.CreateWindowEx(
            User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE, ClassName, "Looppaper", User32.WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            log.Error($"Creating the control window failed ({Marshal.GetLastPInvokeError()})");
            _instance = null;
            return null;
        }

        window.Handle = hwnd;

        // Let TaskbarCreated through even if Looppaper runs elevated while Explorer does not.
        if (window._taskbarCreated != 0)
        {
            User32.ChangeWindowMessageFilterEx(hwnd, window._taskbarCreated, User32.MSGFLT_ALLOW, IntPtr.Zero);
        }

        return window;
    }

    /// <summary>Asks the window to close from any thread, which ends <see cref="RunMessageLoop"/>.</summary>
    public void RequestClose()
    {
        if (Handle != IntPtr.Zero)
        {
            User32.PostMessage(Handle, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Pumps messages until the control window is destroyed.</summary>
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
            // An exception must not unwind into native code; log it and shut down cleanly instead.
            self._log.Error($"Unhandled exception in window message 0x{msg:X}; shutting down", ex);
            User32.PostMessage(hwnd, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return IntPtr.Zero;
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreated && msg != 0)
        {
            _handler.OnTaskbarCreated();
            return IntPtr.Zero;
        }

        switch (msg)
        {
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

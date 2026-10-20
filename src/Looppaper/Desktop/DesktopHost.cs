using Looppaper.Core.Logging;
using Looppaper.Native;
using System.Runtime.InteropServices;

namespace Looppaper.Desktop;

internal enum DesktopLayout
{
    /// <summary>Windows 11 24H2+: the wallpaper WorkerW is a child of Progman, below SHELLDLL_DefView.</summary>
    ProgmanChildWorkerW,

    /// <summary>Windows 10 / 11 before 24H2: a top-level WorkerW sits behind the one hosting SHELLDLL_DefView.</summary>
    ClassicWorkerW,

    /// <summary>No WorkerW could be found; draw directly in Progman below the icons.</summary>
    ProgmanFallback,
}

/// <summary>
/// Locates the Explorer window our wallpaper window must be parented to so it is drawn behind the
/// desktop icons. Same technique as Lively Wallpaper: ask Progman (message 0x052C) to split the
/// desktop into an icon layer and a wallpaper layer, then find the wallpaper layer.
/// </summary>
internal sealed unsafe class DesktopHost
{
    private const uint SpawnTimeoutMs = 1000;

    private static IntPtr _enumDefViewHost;
    private static IntPtr _enumWorkerW;

    private DesktopHost(DesktopLayout layout, IntPtr progman, IntPtr parent, IntPtr defView, IntPtr workerW)
    {
        Layout = layout;
        Progman = progman;
        Parent = parent;
        DefView = defView;
        WorkerW = workerW;
    }

    public DesktopLayout Layout { get; }

    public IntPtr Progman { get; }

    /// <summary>The window ours becomes a child of.</summary>
    public IntPtr Parent { get; }

    public IntPtr DefView { get; }

    public IntPtr WorkerW { get; }

    /// <summary>Extended style our window needs for this layout.</summary>
    public uint ExtendedStyle => Layout == DesktopLayout.ClassicWorkerW ? 0 : User32.WS_EX_LAYERED;

    public bool IsAlive => User32.IsWindow(Parent);

    public static DesktopHost? Find(ILog log)
    {
        var progman = User32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            log.Error("Progman window not found; is Explorer running?");
            return null;
        }

        // 0xD/0x1 is what newer Windows builds expect; 0/0 is the classic form. Sending both is harmless.
        User32.SendMessageTimeout(progman, User32.WM_SPAWN_WORKER, 0xD, 0x1, User32.SMTO_NORMAL, SpawnTimeoutMs, out _);
        User32.SendMessageTimeout(progman, User32.WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, User32.SMTO_NORMAL, SpawnTimeoutMs, out _);

        var childWorkerW = User32.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (childWorkerW != IntPtr.Zero)
        {
            var defView = User32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            log.Info($"Desktop layout: WorkerW is a child of Progman (Windows 11 24H2+); progman=0x{progman:X}, defView=0x{defView:X}, workerW=0x{childWorkerW:X}");
            return new DesktopHost(DesktopLayout.ProgmanChildWorkerW, progman, progman, defView, childWorkerW);
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var (defViewHost, workerW) = FindClassicWorkerW();
            if (workerW != IntPtr.Zero)
            {
                var defView = User32.FindWindowEx(defViewHost, IntPtr.Zero, "SHELLDLL_DefView", null);
                log.Info($"Desktop layout: classic top-level WorkerW; progman=0x{progman:X}, icons host=0x{defViewHost:X}, workerW=0x{workerW:X}");
                return new DesktopHost(DesktopLayout.ClassicWorkerW, progman, workerW, defView, workerW);
            }

            Thread.Sleep(100);
        }

        var progmanDefView = User32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        log.Warn($"Desktop layout: no WorkerW found, falling back to Progman; progman=0x{progman:X}, defView=0x{progmanDefView:X}");
        return new DesktopHost(DesktopLayout.ProgmanFallback, progman, progman, progmanDefView, IntPtr.Zero);
    }

    /// <summary>Finishes attaching a freshly created child window (layering and z-order).</summary>
    public void Attach(IntPtr hwnd, ILog log)
    {
        if (Layout == DesktopLayout.ClassicWorkerW)
        {
            return;
        }

        // A layered child is only composed when it has layered attributes; fully opaque is all we need.
        if (!User32.SetLayeredWindowAttributes(hwnd, 0, 255, User32.LWA_ALPHA))
        {
            log.Warn($"SetLayeredWindowAttributes failed ({Marshal.GetLastPInvokeError()})");
        }

        // Z-order inside Progman: SHELLDLL_DefView (icons) > our window > WorkerW (Explorer's wallpaper).
        const uint flags = User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE;
        if (DefView != IntPtr.Zero && !User32.SetWindowPos(hwnd, DefView, 0, 0, 0, 0, flags))
        {
            log.Warn($"Placing wallpaper below the desktop icons failed ({Marshal.GetLastPInvokeError()})");
        }

        if (WorkerW != IntPtr.Zero && !User32.SetWindowPos(WorkerW, hwnd, 0, 0, 0, 0, flags))
        {
            log.Warn($"Placing Explorer's WorkerW below the wallpaper failed ({Marshal.GetLastPInvokeError()})");
        }
    }

    /// <summary>The whole virtual screen (all monitors) in screen coordinates.</summary>
    public static RECT VirtualScreen()
    {
        var x = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        var y = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        return new RECT(x, y, x + User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN), y + User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN));
    }

    /// <summary>The virtual screen mapped into the parent's client coordinates, i.e. where our child window goes.</summary>
    public RECT ChildBounds()
    {
        var rect = VirtualScreen();
        User32.MapWindowPoints(IntPtr.Zero, Parent, ref rect, 2);
        return rect;
    }

    private static (IntPtr DefViewHost, IntPtr WorkerW) FindClassicWorkerW()
    {
        _enumDefViewHost = IntPtr.Zero;
        _enumWorkerW = IntPtr.Zero;
        User32.EnumWindows(&EnumWindowsProc, IntPtr.Zero);
        return (_enumDefViewHost, _enumWorkerW);
    }

    [UnmanagedCallersOnly]
    private static int EnumWindowsProc(IntPtr hwnd, IntPtr lParam)
    {
        if (User32.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
        {
            return 1;
        }

        // The wallpaper WorkerW is the next top-level WorkerW after the window hosting the icons.
        _enumDefViewHost = hwnd;
        _enumWorkerW = User32.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
        return 0;
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using Looppaper.Core.Control;
using Looppaper.Core.Logging;
using Looppaper.Native;
using static Looppaper.Native.Shell32;

namespace Looppaper.Desktop;

/// <summary>What was picked in the tray icon's menu.</summary>
internal enum TrayAction
{
    None,
    Next,
    Previous,
    Pause,
    Resume,
    OpenCollection,
    EditConfig,
    OpenLog,
    Stop,
}

/// <summary>
/// The optional notification-area icon (config <c>trayIcon</c>, off by default). Left or right click opens a menu;
/// hovering shows what the wallpaper is doing. The icon is drawn here: a play sign, or pause bars while paused.
/// Owned by the control window, whose messages arrive as <see cref="WM_TRAY"/>.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    /// <summary>Callback message of the icon; lParam is the mouse message.</summary>
    public const uint WM_TRAY = User32.WM_APP + 7;

    private const uint IconId = 1;

    private readonly IntPtr _hwnd;
    private readonly ILog _log;
    private readonly IntPtr _playingIcon;
    private readonly IntPtr _pausedIcon;
    private string _tooltip = "Looppaper";
    private bool _paused;
    private bool _added;

    public TrayIcon(IntPtr hwnd, ILog log)
    {
        _hwnd = hwnd;
        _log = log;
        _playingIcon = DrawIcon(paused: false);
        _pausedIcon = DrawIcon(paused: true);
        Add();
    }

    /// <summary>Adds the icon, e.g. again after Explorer restarted (its notification area starts empty).</summary>
    public void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_added)
        {
            _log.Warn("Adding the tray icon failed");
        }
    }

    /// <summary>Changes the tooltip and the play / pause picture when they differ from what is shown.</summary>
    public void Update(string tooltip, bool paused)
    {
        if (tooltip == _tooltip && paused == _paused)
        {
            return;
        }

        _tooltip = tooltip;
        _paused = paused;
        if (_added)
        {
            var data = Data(NIF_ICON | NIF_TIP);
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
    }

    /// <summary>Shows the menu at the mouse and returns what was picked (runs a modal loop; timers keep firing).</summary>
    public TrayAction ShowMenu(ControlReply state)
    {
        var menu = User32.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return TrayAction.None;
        }

        try
        {
            var slideshow = state.State.HasFlag(ControlState.Slideshow) ? User32.MF_STRING : User32.MF_GRAYED;
            var pausedByUser = state.State.HasFlag(ControlState.PausedByUser);
            User32.AppendMenu(menu, User32.MF_GRAYED, 0, Shorten(state.Describe(), 80));
            User32.AppendMenu(menu, User32.MF_SEPARATOR, 0, null);
            User32.AppendMenu(menu, slideshow, (nuint)TrayAction.Next, "&Next picture");
            User32.AppendMenu(menu, slideshow, (nuint)TrayAction.Previous, "P&revious picture");
            User32.AppendMenu(menu, User32.MF_STRING, (nuint)(pausedByUser ? TrayAction.Resume : TrayAction.Pause), pausedByUser ? "&Resume" : "&Pause");
            User32.AppendMenu(menu, User32.MF_SEPARATOR, 0, null);
            User32.AppendMenu(menu, User32.MF_STRING, (nuint)TrayAction.OpenCollection, "Open the &collection folder");
            User32.AppendMenu(menu, User32.MF_STRING, (nuint)TrayAction.EditConfig, "&Edit config.json");
            User32.AppendMenu(menu, User32.MF_STRING, (nuint)TrayAction.OpenLog, "Open the &log");
            User32.AppendMenu(menu, User32.MF_SEPARATOR, 0, null);
            User32.AppendMenu(menu, User32.MF_STRING, (nuint)TrayAction.Stop, "&Stop Looppaper");

            User32.GetCursorPos(out var point);

            // Without this the menu does not close when clicking elsewhere (documented TrackPopupMenu quirk).
            User32.SetForegroundWindow(_hwnd);
            var picked = User32.TrackPopupMenu(menu, User32.TPM_RETURNCMD | User32.TPM_NONOTIFY | User32.TPM_RIGHTBUTTON | User32.TPM_BOTTOMALIGN, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
            User32.PostMessage(_hwnd, User32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return Enum.IsDefined((TrayAction)picked) ? (TrayAction)picked : TrayAction.None;
        }
        finally
        {
            User32.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        DestroyIcon(_playingIcon);
        DestroyIcon(_pausedIcon);
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = WM_TRAY,
            hIcon = _paused ? _pausedIcon : _playingIcon,
        };
        CopyText(_tooltip, data.szTip, 128);
        return data;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>A rounded square with a play sign (blue) or pause bars (gray); Windows scales it to the tray size.</summary>
    private static IntPtr DrawIcon(bool paused)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var back = new SolidBrush(paused ? Color.FromArgb(0x6B, 0x72, 0x80) : Color.FromArgb(0x25, 0x63, 0xEB));
            using var path = new GraphicsPath();
            const int r = 8;
            path.AddArc(1, 1, r * 2, r * 2, 180, 90);
            path.AddArc(size - 1 - r * 2, 1, r * 2, r * 2, 270, 90);
            path.AddArc(size - 1 - r * 2, size - 1 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(1, size - 1 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(back, path);

            if (paused)
            {
                g.FillRectangle(Brushes.White, 10, 9, 4, 14);
                g.FillRectangle(Brushes.White, 18, 9, 4, 14);
            }
            else
            {
                g.FillPolygon(Brushes.White, new PointF[] { new(12, 8), new(12, 24), new(24, 16) });
            }
        }

        return bitmap.GetHicon();
    }
}

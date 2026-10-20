using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Media;
using Looppaper.Desktop;
using Looppaper.Native;
using Looppaper.Rendering;

namespace Looppaper;

/// <summary>Owns the wallpaper window and everything drawn or played while it is alive.</summary>
internal sealed class WallpaperApp : IWallpaperWindowHandler
{
    private readonly LooppaperConfig _config;
    private readonly ILog _log;
    private readonly IntPtr _backgroundBrush;
    private readonly FrameCache _frame = new();
    private WallpaperWindow? _window;
    private DesktopHost? _host;
    private bool _shutDown;

    public WallpaperApp(LooppaperConfig config, ILog log)
    {
        _config = config;
        _log = log;
        _backgroundBrush = Gdi32.CreateSolidBrush(config.ResolveBackgroundColor().ToColorRef());
    }

    public int Run()
    {
        WallpaperRestorer.Capture(_log);
        _host = DesktopHost.Find(_log);
        if (_host is null)
        {
            return 1;
        }

        _window = WallpaperWindow.Create(_host, this, _log);
        if (_window is null)
        {
            return 1;
        }

        var scanner = new MediaScanner(_log);
        var pictures = scanner.Scan(_config.ResolveCollectionFolder(), MediaKind.Picture, _config.MaxPictureSizeMb);
        ShowPicture(_config.ResolvePicture() ?? pictures.FirstOrDefault());

        WallpaperWindow.RunMessageLoop();
        Shutdown();
        return 0;
    }

    /// <summary>Thread-safe request to close the window and leave the message loop.</summary>
    public void RequestStop()
    {
        _log.Info("Stop requested");
        _window?.RequestClose();
    }

    public void OnPaint(IntPtr hdc, in RECT area)
    {
        if (_frame.HasFrame)
        {
            _frame.Paint(hdc, area);
        }
        else
        {
            User32.FillRect(hdc, area, _backgroundBrush);
        }
    }

    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam) => false;

    public void OnEndSession()
    {
        _log.Info("Windows session is ending");
        Shutdown();
    }

    private void ShowPicture(string? path)
    {
        var bounds = _host!.ChildBounds();
        var monitors = Monitors.RelativeToVirtualScreen();
        var hbitmap = PictureRenderer.Render(path, bounds.Width, bounds.Height, monitors, _config.Scaling, _config.ResolveBackgroundColor(), _log);
        if (hbitmap == IntPtr.Zero)
        {
            return;
        }

        _frame.Swap(hbitmap);
        _window!.Invalidate();
        _log.Info(path is null ? "Showing background color" : $"Showing {path} on {monitors.Count} monitor(s): {string.Join(", ", monitors)}");
    }

    private void Shutdown()
    {
        if (_shutDown)
        {
            return;
        }

        _shutDown = true;
        _frame.Dispose();
        Gdi32.DeleteObject(_backgroundBrush);
        WallpaperRestorer.Restore(_log);
        _log.Info("Looppaper stopped");
    }
}

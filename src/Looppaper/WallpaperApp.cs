using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Desktop;
using Looppaper.Native;

namespace Looppaper;

/// <summary>Owns the wallpaper window and everything drawn or played while it is alive.</summary>
internal sealed class WallpaperApp : IWallpaperWindowHandler
{
    private readonly LooppaperConfig _config;
    private readonly ILog _log;
    private readonly IntPtr _backgroundBrush;
    private WallpaperWindow? _window;

    public WallpaperApp(LooppaperConfig config, ILog log)
    {
        _config = config;
        _log = log;
        _backgroundBrush = Gdi32.CreateSolidBrush(config.ResolveBackgroundColor().ToColorRef());
    }

    public int Run()
    {
        WallpaperRestorer.Capture(_log);
        var host = DesktopHost.Find(_log);
        if (host is null)
        {
            return 1;
        }

        _window = WallpaperWindow.Create(host, this, _log);
        if (_window is null)
        {
            return 1;
        }

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

    public void OnPaint(IntPtr hdc, in RECT client)
    {
        User32.FillRect(hdc, client, _backgroundBrush);
    }

    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam) => false;

    public void OnEndSession()
    {
        _log.Info("Windows session is ending");
        Shutdown();
    }

    private void Shutdown()
    {
        Gdi32.DeleteObject(_backgroundBrush);
        WallpaperRestorer.Restore(_log);
        _log.Info("Looppaper stopped");
    }
}

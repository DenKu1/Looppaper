using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Media;
using Looppaper.Desktop;
using Looppaper.Native;
using Looppaper.Rendering;
using Looppaper.Video;

namespace Looppaper;

/// <summary>Owns the wallpaper window and whatever is shown in it: a looping video, or pictures.</summary>
internal sealed class WallpaperApp : IWallpaperWindowHandler
{
    private readonly LooppaperConfig _config;
    private readonly ILog _log;
    private readonly IntPtr _backgroundBrush;
    private readonly FrameCache _frame = new();
    private WallpaperWindow? _window;
    private DesktopHost? _host;
    private PictureController? _pictures;
    private VideoController? _video;
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

        if (!TryStartVideo())
        {
            StartPictures();
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

    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam) =>
        _video?.OnMessage(msg, wParam, lParam) == true || _pictures?.OnMessage(msg, wParam, lParam) == true;

    public void OnEndSession()
    {
        _log.Info("Windows session is ending");
        Shutdown();
    }

    private bool TryStartVideo()
    {
        if (!_config.Video.Enabled)
        {
            _log.Info("Video disabled; showing pictures");
            return false;
        }

        var path = FindVideo();
        if (path is null)
        {
            return false;
        }

        _video = new VideoController(_config, _log, _window!);
        if (!_video.Start(path, Monitors.RelativeToVirtualScreen()))
        {
            _video = null;
            _log.Warn("Falling back to pictures");
            return false;
        }

        _video.Failed += () =>
        {
            _video = null;
            _log.Warn("Falling back to pictures");
            StartPictures();
        };
        ResourceSaver.Trim();
        return true;
    }

    /// <summary>The configured video if it is usable, otherwise the first .mp4 in the collection.</summary>
    private string? FindVideo()
    {
        var maxMb = _config.Video.MaxFileSizeMb;
        var configured = _config.ResolveVideo();
        if (configured is not null)
        {
            string? problem = null;
            if (!MediaScanner.IsSupported(configured, MediaKind.Video))
            {
                problem = "is not an .mp4 file";
            }
            else if (!File.Exists(configured))
            {
                problem = "does not exist";
            }
            else if (new FileInfo(configured).Length > maxMb * MediaScanner.BytesPerMb)
            {
                problem = $"is {new FileInfo(configured).Length / (double)MediaScanner.BytesPerMb:F1} MB, above the {maxMb} MB video limit, so it will not be played";
            }

            if (problem is null)
            {
                return configured;
            }

            _log.Warn($"Configured video {configured} {problem}; looking for another video in the collection");
        }

        var videos = new MediaScanner(_log).Scan(_config.ResolveCollectionFolder(), MediaKind.Video, maxMb);
        if (videos.Count == 0)
        {
            _log.Info("No video in the collection; showing pictures");
            return null;
        }

        return videos[0];
    }

    private void StartPictures()
    {
        if (_pictures is not null || _window is null || _shutDown)
        {
            return;
        }

        _pictures = new PictureController(_config, _log, _window, _host!, _frame);
        _pictures.FrameShown += ResourceSaver.Trim;
        _pictures.Start();
    }

    private void Shutdown()
    {
        if (_shutDown)
        {
            return;
        }

        _shutDown = true;
        _video?.Stop();
        _pictures?.Stop();
        _frame.Dispose();
        Gdi32.DeleteObject(_backgroundBrush);
        WallpaperRestorer.Restore(_log);
        _log.Info("Looppaper stopped");
    }
}

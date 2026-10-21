using Looppaper.Core.Compression;
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
    /// <summary>Posted by the compression worker when the compressed copy of the playing video is ready.</summary>
    private const uint WM_COMPRESSED_VIDEO_READY = User32.WM_APP + 3;

    private readonly LooppaperConfig _config;
    private readonly ILog _log;
    private readonly IntPtr _backgroundBrush;
    private readonly FrameCache _frame = new();
    private WallpaperWindow? _window;
    private DesktopHost? _host;
    private PictureController? _pictures;
    private VideoController? _video;
    private readonly CancellationTokenSource _cancel = new();
    private Task? _compression;
    private volatile string? _compressedPath;
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

    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_COMPRESSED_VIDEO_READY:
                SwitchToCompressedVideo();
                return true;
            case PictureController.WM_FRAME_READY when _pictures is null:
                // A picture finished rendering after the switch to video; drop it.
                Gdi32.DeleteObject(lParam);
                return true;
            default:
                return _video?.OnMessage(msg, wParam, lParam) == true || _pictures?.OnMessage(msg, wParam, lParam) == true;
        }
    }

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

        var choice = FindVideo();
        var started = choice.PlayPath is { } path && StartVideo(path);
        if (choice.ToCompress is { } original)
        {
            // The original plays meanwhile (or pictures, if it could not be started).
            CompressInBackground(original);
        }

        return started;
    }

    private bool StartVideo(string path)
    {
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

    /// <summary>
    /// The configured video if it is usable, otherwise the first .mp4 in the collection; its compressed copy
    /// when there is one (see <see cref="VideoCompressionFlow"/>).
    /// </summary>
    private VideoChoice FindVideo()
    {
        var choice = VideoChooser.Choose(VideoCandidates(), key => VideoCompressionFlow.FindPlayable(key, _log), _config.Video.Compress, _log);
        if (choice.PlayPath is null)
        {
            _log.Info("No video in the collection; showing pictures");
        }

        return choice;
    }

    /// <summary>
    /// Compresses the playing video on a worker thread (with a Windows notification, never a question) and
    /// switches to the compressed copy once it is ready.
    /// </summary>
    private void CompressInBackground(VideoFileKey key)
    {
        var hwnd = _window!.Handle;
        var token = _cancel.Token;
        _compression = Task.Run(() =>
        {
            try
            {
                var path = VideoCompressionFlow.Resolve(key.FullPath, _config, _log, CompressionMode.Background, hwnd, token);
                if (path is not null && !token.IsCancellationRequested && !string.Equals(path, key.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _compressedPath = path;
                    User32.PostMessage(hwnd, WM_COMPRESSED_VIDEO_READY, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Compressing {key.FullPath} failed", ex);
            }
        }, CancellationToken.None);
    }

    /// <summary>Runs on the UI thread: replaces whatever is shown with the compressed video.</summary>
    private void SwitchToCompressedVideo()
    {
        var path = _compressedPath;
        if (_shutDown || path is null)
        {
            return;
        }

        _log.Info($"Compressed video ready; switching to {path}");
        _video?.Stop();
        _video = null;
        _pictures?.Stop();
        _pictures = null;
        if (!StartVideo(path))
        {
            StartPictures();
        }
    }

    /// <summary>The configured video (when it exists), then every .mp4 in the collection.</summary>
    private IEnumerable<VideoFileKey> VideoCandidates()
    {
        var configured = _config.ResolveVideo();
        if (configured is not null)
        {
            var key = MediaScanner.IsSupported(configured, MediaKind.Video) ? VideoFileKey.TryFromFile(configured) : null;
            if (key is not null)
            {
                yield return key;
            }
            else
            {
                var problem = MediaScanner.IsSupported(configured, MediaKind.Video) ? "does not exist" : "is not an .mp4 file";
                _log.Warn($"Configured video {configured} {problem}; looking for another video in the collection");
            }
        }

        // No size limit: every video plays (compressed first when video.compress is on).
        foreach (var path in new MediaScanner(_log).Scan(_config.ResolveCollectionFolder(), MediaKind.Video, int.MaxValue))
        {
            if (VideoFileKey.TryFromFile(path) is { } key)
            {
                yield return key;
            }
        }
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
        _cancel.Cancel();
        if (_compression is { IsCompleted: false } compression && !compression.Wait(TimeSpan.FromSeconds(5)))
        {
            _log.Warn("Video compression did not stop within 5 seconds");
        }

        _video?.Stop();
        _pictures?.Stop();
        _frame.Dispose();
        Gdi32.DeleteObject(_backgroundBrush);
        WallpaperRestorer.Restore(_log);
        _log.Info("Looppaper stopped");
    }
}

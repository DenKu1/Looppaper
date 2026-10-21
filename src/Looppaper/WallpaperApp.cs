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
internal sealed class WallpaperApp : IWallpaperWindowHandler, IControlWindowHandler
{
    /// <summary>Posted by the compression worker when the compressed copy of the playing video is ready.</summary>
    private const uint WM_COMPRESSED_VIDEO_READY = User32.WM_APP + 3;

    /// <summary>Posted when the wallpaper window was destroyed from outside (Explorer went away).</summary>
    private const uint WM_WALLPAPER_LOST = User32.WM_APP + 4;

    private const nuint RecoverTimerId = 10;

    /// <summary>Gives a restarting Explorer a moment to build its desktop before attaching to it.</summary>
    private const uint RecoverDelayMs = 1000;

    /// <summary>How often to look for Explorer's desktop again while it is missing.</summary>
    private const uint RecoverRetryMs = 2000;

    private readonly LooppaperConfig _config;
    private readonly ILog _log;
    private readonly IntPtr _backgroundBrush;
    private readonly FrameCache _frame = new();
    private ControlWindow? _control;
    private WallpaperWindow? _window;
    private DesktopHost? _host;
    private PictureController? _pictures;
    private VideoController? _video;
    private readonly CancellationTokenSource _cancel = new();
    private Task? _compression;
    private volatile string? _compressedPath;
    private bool _recovering;
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
        _control = ControlWindow.Create(this, _log);
        if (_control is null)
        {
            return 1;
        }

        if (AttachToDesktop())
        {
            StartContent();
        }
        else
        {
            // E.g. started at sign-in before Explorer's desktop exists: wait for it instead of giving up.
            BeginRecovery("the desktop is not available yet");
        }

        ControlWindow.RunMessageLoop();
        Shutdown();
        return 0;
    }

    /// <summary>Thread-safe request to close the windows and leave the message loop.</summary>
    public void RequestStop()
    {
        _log.Info("Stop requested");
        _control?.RequestClose();
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

    public void OnWindowLost()
    {
        if (_control is { Handle: var hwnd } && hwnd != IntPtr.Zero)
        {
            User32.PostMessage(hwnd, WM_WALLPAPER_LOST, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void OnTaskbarCreated()
    {
        if (_recovering || _shutDown)
        {
            return;
        }

        if (IsAttached())
        {
            _log.Info("Taskbar re-created; the wallpaper window is still attached to the desktop");
            return;
        }

        BeginRecovery("Explorer restarted");
    }

    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_COMPRESSED_VIDEO_READY:
                SwitchToCompressedVideo();
                return true;
            case WM_WALLPAPER_LOST:
                BeginRecovery("the wallpaper window was destroyed");
                return true;
            case User32.WM_TIMER when (nuint)wParam == RecoverTimerId:
                Recover();
                return true;
            case PictureController.WM_FRAME_READY when _pictures is null:
                // A picture finished rendering after the switch to video (or a rebuild); drop it.
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

    /// <summary>True when our window still lives in the current Explorer desktop.</summary>
    private bool IsAttached() =>
        _window is { IsAlive: true } && _host is { IsAlive: true } host && host.Progman == User32.FindWindow("Progman", null);

    /// <summary>(Re)creates the wallpaper window in Explorer's current desktop.</summary>
    private bool AttachToDesktop()
    {
        _window?.Destroy();
        _window = null;
        _host = DesktopHost.Find(_log);
        if (_host is null)
        {
            return false;
        }

        _window = WallpaperWindow.Create(_host, this, _log);
        return _window is not null;
    }

    /// <summary>Stops what is shown and waits for Explorer's desktop to come back (see <see cref="Recover"/>).</summary>
    private void BeginRecovery(string reason)
    {
        if (_shutDown || _recovering)
        {
            return;
        }

        _log.Warn($"Lost the desktop ({reason}); waiting for Explorer to re-attach the wallpaper");
        _recovering = true;
        StopContent();
        User32.SetTimer(_control!.Handle, RecoverTimerId, RecoverDelayMs, IntPtr.Zero);
    }

    private void Recover()
    {
        User32.KillTimer(_control!.Handle, RecoverTimerId);
        if (_shutDown)
        {
            return;
        }

        // Quietly keep waiting while Explorer is not running at all.
        if (User32.FindWindow("Progman", null) == IntPtr.Zero || !AttachToDesktop())
        {
            User32.SetTimer(_control.Handle, RecoverTimerId, RecoverRetryMs, IntPtr.Zero);
            return;
        }

        _recovering = false;
        _log.Info("Re-attached to the desktop; restarting the wallpaper");
        StartContent();
    }

    private void StartContent()
    {
        if (!TryStartVideo())
        {
            StartPictures();
        }
    }

    private void StopContent()
    {
        _video?.Stop();
        _video = null;
        _pictures?.Stop();
        _pictures = null;
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
        if (choice.ToCompress is { } original && _compression is not { IsCompleted: false })
        {
            // The original plays meanwhile (or pictures, if it could not be started).
            CompressInBackground(original);
        }

        return started;
    }

    private bool StartVideo(string path)
    {
        _video = new VideoController(_config, _log, _window!, _control!.Handle);
        if (!_video.Start(path, Monitors.RelativeToVirtualScreen()))
        {
            _video = null;
            _log.Warn("Falling back to pictures");
            return false;
        }

        var video = _video;
        video.Failed += () =>
        {
            if (!ReferenceEquals(_video, video))
            {
                return;
            }

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
        var hwnd = _control!.Handle;
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
        if (_shutDown || _recovering || path is null)
        {
            return; // after recovery the compressed copy is found through its record
        }

        _log.Info($"Compressed video ready; switching to {path}");
        StopContent();
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

        _pictures = new PictureController(_config, _log, _window, _control!.Handle, _host!, _frame);
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

        StopContent();
        _window?.Destroy();
        _window = null;
        _frame.Dispose();
        Gdi32.DeleteObject(_backgroundBrush);
        WallpaperRestorer.Restore(_log);
        _log.Info("Looppaper stopped");
    }
}

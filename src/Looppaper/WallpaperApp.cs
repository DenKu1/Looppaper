using System.Diagnostics;
using Looppaper.Core;
using Looppaper.Core.Compression;
using Looppaper.Core.Configuration;
using Looppaper.Core.Control;
using Looppaper.Core.Logging;
using Looppaper.Core.Media;
using Looppaper.Core.Playback;
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

    /// <summary>Posted by the file watchers (thread-pool threads) when config.json or the collection folder changed.</summary>
    private const uint WM_CONFIG_CHANGED = User32.WM_APP + 5;
    private const uint WM_COLLECTION_CHANGED = User32.WM_APP + 6;

    private const nint SPI_SETWORKAREA = 0x2F;

    private const nuint RecoverTimerId = 10;

    /// <summary>Gives a restarting Explorer a moment to build its desktop before attaching to it.</summary>
    private const uint RecoverDelayMs = 1000;

    /// <summary>How often to look for Explorer's desktop again while it is missing.</summary>
    private const uint RecoverRetryMs = 2000;

    private const nuint FullscreenTimerId = 11;

    /// <summary>How often to look for a full-screen app in the foreground.</summary>
    private const uint FullscreenPollMs = 2000;

    private const nuint DisplayTimerId = 12;

    /// <summary>Display changes arrive in bursts (one per monitor, resolution, DPI, work area); act once they settle.</summary>
    private const uint DisplaySettleMs = 1500;

    private const nuint ConfigTimerId = 13;
    private const nuint CollectionTimerId = 14;

    /// <summary>Editors save in several steps; read the config once they are done.</summary>
    private const uint ConfigSettleMs = 500;

    /// <summary>Copying a batch of pictures takes a while; look at the collection once it is quiet.</summary>
    private const uint CollectionSettleMs = 2000;

    private readonly string _configPath;
    private readonly Func<LooppaperConfig?> _reloadConfig;
    private readonly ILog _log;
    private LooppaperConfig _config;
    private IntPtr _backgroundBrush;
    private readonly FrameCache _frame = new();
    private ControlWindow? _control;
    private PowerWatcher? _power;
    private TrayIcon? _tray;
    private FileChangeWatcher? _configWatcher;
    private FileChangeWatcher? _collectionWatcher;
    private WallpaperWindow? _window;
    private DesktopHost? _host;
    private PictureController? _pictures;
    private VideoController? _video;
    private readonly PauseState _pause = new();
    private readonly CancellationTokenSource _cancel = new();
    private Task? _compression;
    private volatile string? _compressedPath;

    /// <summary>Virtual screen and monitors the current window and content were built for.</summary>
    private string? _displayLayout;
    private bool _recovering;
    private bool _shutDown;

    /// <param name="reloadConfig">Re-reads <paramref name="configPath"/> with the command-line overrides applied; null keeps the current settings.</param>
    public WallpaperApp(LooppaperConfig config, string configPath, Func<LooppaperConfig?> reloadConfig, ILog log)
    {
        _config = config;
        _configPath = configPath;
        _reloadConfig = reloadConfig;
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

        _power = new PowerWatcher(_control.Handle, _log);
        _power.Changed += (_, _) => SyncPauseSettings();
        _power.Resumed += RestartVideoAfterSleep;
        SyncPauseSettings();
        SyncTrayIcon();

        var configFolder = Path.GetDirectoryName(_configPath);
        if (configFolder is not null)
        {
            _configWatcher = FileChangeWatcher.Start(
                configFolder, Path.GetFileName(_configPath), path => string.Equals(path, _configPath, StringComparison.OrdinalIgnoreCase),
                _control.Handle, WM_CONFIG_CHANGED, _log);
        }

        WatchCollection();

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
        if (!_shutDown)
        {
            _tray?.Add(); // the new notification area starts empty
        }

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
            case TrayIcon.WM_TRAY:
                OnTrayMessage((uint)lParam & 0xFFFF);
                return true;
            case User32.WM_TIMER when (nuint)wParam == RecoverTimerId:
                Recover();
                return true;
            case User32.WM_TIMER when (nuint)wParam == FullscreenTimerId:
                CheckFullscreen();
                return true;
            case User32.WM_DISPLAYCHANGE or User32.WM_DPICHANGED:
            case User32.WM_SETTINGCHANGE when wParam == SPI_SETWORKAREA:
                User32.SetTimer(_control!.Handle, DisplayTimerId, DisplaySettleMs, IntPtr.Zero);
                return false;
            case User32.WM_TIMER when (nuint)wParam == DisplayTimerId:
                User32.KillTimer(_control!.Handle, DisplayTimerId);
                OnDisplayChanged();
                return true;
            case WM_CONFIG_CHANGED:
                User32.SetTimer(_control!.Handle, ConfigTimerId, ConfigSettleMs, IntPtr.Zero);
                return true;
            case WM_COLLECTION_CHANGED:
                User32.SetTimer(_control!.Handle, CollectionTimerId, CollectionSettleMs, IntPtr.Zero);
                return true;
            case User32.WM_TIMER when (nuint)wParam == ConfigTimerId:
                User32.KillTimer(_control!.Handle, ConfigTimerId);
                ReloadConfig();
                return true;
            case User32.WM_TIMER when (nuint)wParam == CollectionTimerId:
                User32.KillTimer(_control!.Handle, CollectionTimerId);
                OnCollectionChanged();
                return true;
            case Power.WM_WTSSESSION_CHANGE or Power.WM_POWERBROADCAST:
                return _power?.OnMessage(msg, wParam, lParam) == true;
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

    public ControlReply OnControl(ControlCommand command)
    {
        var applicable = true;
        switch (command)
        {
            case ControlCommand.Pause or ControlCommand.Resume:
                SetPausedByUser(command == ControlCommand.Pause, "command line");
                break;
            case ControlCommand.Next or ControlCommand.Previous:
                applicable = _pictures?.Step(backwards: command == ControlCommand.Previous) == true;
                break;
        }

        return CurrentState(applicable);
    }

    /// <summary>The user's own pause (<c>--pause</c> / <c>--resume</c>), on top of the automatic ones.</summary>
    private void SetPausedByUser(bool paused, string source)
    {
        if (_pause.Set(PauseReason.User, paused))
        {
            _log.Info($"{(paused ? "Paused" : "Resumed")} on request ({source})");
            ApplyPause();
        }
    }

    private ControlReply CurrentState(bool applicable = true)
    {
        var state = ControlState.Running;
        if (_recovering)
        {
            state |= ControlState.WaitingForDesktop;
        }

        if (_video is not null)
        {
            state |= ControlState.Video;
        }
        else if (_pictures is not null)
        {
            state |= ControlState.Pictures | (_config.Mode == WallpaperMode.Slideshow ? ControlState.Slideshow : ControlState.None);
        }

        if (_pause.Reasons.HasFlag(PauseReason.User))
        {
            state |= ControlState.PausedByUser;
        }

        if (_pause.CoveredMonitors.Count > 0)
        {
            state |= ControlState.MonitorCovered;
        }

        if (!applicable)
        {
            state |= ControlState.NotApplicable;
        }

        return new ControlReply(state, _pause.Reasons & ~PauseReason.User);
    }

    /// <summary>Shows or removes the tray icon to match <c>trayIcon</c> (at start and after the config was reloaded).</summary>
    private void SyncTrayIcon()
    {
        if (_config.TrayIcon && _tray is null)
        {
            _tray = new TrayIcon(_control!.Handle, _log);
            _log.Info("Tray icon shown");
            RefreshTray();
        }
        else if (!_config.TrayIcon && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
            _log.Info("Tray icon removed");
        }
    }

    private void RefreshTray()
    {
        if (_tray is not null)
        {
            var state = CurrentState();
            _tray.Update("Looppaper: " + state.Describe(), state.IsPaused);
        }
    }

    private void OnTrayMessage(uint mouseMessage)
    {
        if (_tray is null || _shutDown)
        {
            return;
        }

        switch (mouseMessage)
        {
            case User32.WM_MOUSEMOVE:
                RefreshTray(); // the tooltip is about to show
                break;
            case User32.WM_LBUTTONUP or User32.WM_RBUTTONUP:
                RunTrayAction(_tray.ShowMenu(CurrentState()));
                break;
        }
    }

    private void RunTrayAction(TrayAction action)
    {
        switch (action)
        {
            case TrayAction.Next or TrayAction.Previous:
                _pictures?.Step(backwards: action == TrayAction.Previous);
                break;
            case TrayAction.Pause or TrayAction.Resume:
                SetPausedByUser(action == TrayAction.Pause, "tray icon");
                break;
            case TrayAction.OpenCollection:
                var folder = _config.ResolveCollectionFolder();
                Open(folder, create: true);
                break;
            case TrayAction.EditConfig:
                Open(_configPath, editor: true);
                break;
            case TrayAction.OpenLog:
                Open(AppPaths.LogFile, editor: true);
                break;
            case TrayAction.Stop:
                RequestStop();
                break;
        }
    }

    /// <summary>Opens a folder in Explorer, or a file with its default app (Notepad when there is none).</summary>
    private void Open(string path, bool editor = false, bool create = false)
    {
        try
        {
            if (create)
            {
                Directory.CreateDirectory(path);
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception) when (editor)
            {
                // No app is associated with the file type (.json, .log).
                Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Opening {path} failed: {ex.Message}");
        }
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
        _displayLayout = DisplayLayout();
        if (!TryStartVideo())
        {
            StartPictures();
        }

        RefreshTray();
    }

    /// <summary>Monitors were added, removed or rearranged, or the resolution or DPI changed: rebuild for the new layout.</summary>
    private void OnDisplayChanged()
    {
        if (_recovering || _shutDown)
        {
            return;
        }

        var layout = DisplayLayout();
        if (layout == _displayLayout && IsAttached())
        {
            return;
        }

        _log.Info($"Display layout changed to {layout}; rebuilding the wallpaper");
        StopContent();
        _pause.SetCovered([]); // monitor rectangles changed; the next full-screen check fills it again
        if (!AttachToDesktop())
        {
            BeginRecovery("the desktop could not be found after a display change");
            return;
        }

        StartContent();
    }

    private static string DisplayLayout() =>
        $"virtual screen {DesktopHost.VirtualScreen()}, monitors {string.Join(", ", Monitors.RelativeToVirtualScreen())}";

    /// <summary>
    /// Brings <see cref="_pause"/> in line with the power watcher's state and the <c>pause</c> settings, and starts
    /// or stops the full-screen poll. Used at start, on every power change and after the config was reloaded.
    /// </summary>
    private void SyncPauseSettings()
    {
        var changed = false;
        var active = _power?.Active ?? PauseReason.None;
        foreach (var reason in Enum.GetValues<PauseReason>())
        {
            if (reason is not (PauseReason.None or PauseReason.ExclusiveFullscreen or PauseReason.User))
            {
                changed |= _pause.Set(reason, active.HasFlag(reason) && _config.Pause.Allows(reason));
            }
        }

        if (_config.Pause.OnFullscreen)
        {
            User32.SetTimer(_control!.Handle, FullscreenTimerId, FullscreenPollMs, IntPtr.Zero);
        }
        else
        {
            User32.KillTimer(_control!.Handle, FullscreenTimerId);
            changed |= _pause.SetCovered([]);
            changed |= _pause.Set(PauseReason.ExclusiveFullscreen, false);
        }

        if (changed)
        {
            ApplyPause();
        }
    }

    /// <summary>Watches the collection folder for added, changed and removed pictures and videos.</summary>
    private void WatchCollection()
    {
        _collectionWatcher?.Dispose();
        _collectionWatcher = FileChangeWatcher.Start(
            _config.ResolveCollectionFolder(), "*",
            path => MediaScanner.IsSupported(path, MediaKind.Picture) || MediaScanner.IsSupported(path, MediaKind.Video),
            _control!.Handle, WM_COLLECTION_CHANGED, _log);
    }

    /// <summary>config.json changed on disk: apply the new settings without a restart.</summary>
    private void ReloadConfig()
    {
        if (_shutDown)
        {
            return;
        }

        var fresh = _reloadConfig();
        if (fresh is null)
        {
            return;
        }

        if (ConfigLoader.Serialize(fresh) == ConfigLoader.Serialize(_config))
        {
            _log.Info("Config file changed; the settings are the same");
            return;
        }

        if (fresh.TrayIcon != _config.TrayIcon)
        {
            var trayIcon = _config.TrayIcon;
            _config.TrayIcon = fresh.TrayIcon;
            var onlyTray = ConfigLoader.Serialize(fresh) == ConfigLoader.Serialize(_config);
            _config.TrayIcon = trayIcon;
            if (onlyTray)
            {
                // Only the tray icon changed: no need to restart the video or the slideshow.
                _config = fresh;
                SyncTrayIcon();
                return;
            }
        }

        var folderChanged = !string.Equals(fresh.ResolveCollectionFolder(), _config.ResolveCollectionFolder(), StringComparison.OrdinalIgnoreCase);
        _log.Info($"Config reloaded: collection {fresh.ResolveCollectionFolder()}, mode {fresh.Mode}, interval {fresh.IntervalSeconds}s, scaling {fresh.Scaling}, video {(fresh.Video.Enabled ? "on" : "off")}");
        _config = fresh;
        var oldBrush = _backgroundBrush;
        _backgroundBrush = Gdi32.CreateSolidBrush(fresh.ResolveBackgroundColor().ToColorRef());
        Gdi32.DeleteObject(oldBrush);
        if (folderChanged)
        {
            WatchCollection();
        }

        SyncPauseSettings();
        SyncTrayIcon();
        if (_recovering)
        {
            return; // the new settings apply once the desktop is back
        }

        StopContent();
        StartContent();
    }

    /// <summary>
    /// Pictures or videos were added, changed or removed: switch to a video that appeared (or away from one that
    /// went), and let the pictures pick up the new set.
    /// </summary>
    private void OnCollectionChanged()
    {
        if (_shutDown || _recovering)
        {
            return;
        }

        _log.Info("Collection folder changed");
        var playing = _video?.Path;
        var wanted = _config.Video.Enabled ? FindVideo().PlayPath : null;
        if (!string.Equals(playing, wanted, StringComparison.OrdinalIgnoreCase))
        {
            StopContent();
            StartContent();
            return;
        }

        _pictures?.Refresh();
    }

    /// <summary>Polled: pauses the monitor a full-screen window covers, or everything for an exclusive full-screen game.</summary>
    private void CheckFullscreen()
    {
        var (covered, exclusive) = FullscreenDetector.Check();
        var changed = _pause.SetCovered(covered is { } monitor ? [monitor] : []);
        changed |= _pause.Set(PauseReason.ExclusiveFullscreen, exclusive);
        if (changed)
        {
            ApplyPause();
        }
    }

    /// <summary>The Media Engine can lose its GPU device across sleep; start the video again to be safe.</summary>
    private void RestartVideoAfterSleep()
    {
        if (_video is null || _recovering || _shutDown)
        {
            return;
        }

        _log.Info("Restarting the video after sleep");
        StopContent();
        StartContent();
    }

    /// <summary>Applies <see cref="_pause"/> to whatever is shown.</summary>
    private void ApplyPause()
    {
        _log.Info($"Wallpaper {_pause}");
        _video?.ApplyPause(_pause);
        _pictures?.SetPaused(_pause.IsPausedEverywhere(Monitors.RelativeToVirtualScreen().ToList()));
        RefreshTray();
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
        video.ApplyPause(_pause);
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
        _pictures.SetPaused(_pause.IsPausedEverywhere(Monitors.RelativeToVirtualScreen().ToList()));
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

        _power?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _configWatcher?.Dispose();
        _collectionWatcher?.Dispose();
        StopContent();
        _window?.Destroy();
        _window = null;
        _frame.Dispose();
        Gdi32.DeleteObject(_backgroundBrush);
        WallpaperRestorer.Restore(_log);
        _log.Info("Looppaper stopped");
    }
}

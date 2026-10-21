using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Media;
using Looppaper.Desktop;
using Looppaper.Native;

namespace Looppaper.Rendering;

/// <summary>
/// Decides which picture is on screen. Single mode renders once; slideshow mode advances on a
/// window timer. Each frame is rendered on a thread-pool thread and handed back to the UI thread
/// with a posted message, so the message loop never blocks and only one frame is kept in memory.
/// </summary>
internal sealed class PictureController
{
    public const uint WM_FRAME_READY = User32.WM_APP + 1;

    private const nuint TimerId = 1;

    /// <summary>How often single mode looks for a picture while it has nothing to show.</summary>
    private const uint EmptyRescanMs = 30_000;

    private static int _nextGeneration;

    private readonly LooppaperConfig _config;
    private readonly ILog _log;
    private readonly WallpaperWindow _window;
    private readonly IntPtr _messageWindow;
    private readonly int _generation = Interlocked.Increment(ref _nextGeneration);
    private readonly DesktopHost _host;
    private readonly FrameCache _frame;
    private readonly MediaScanner _scanner;
    private readonly MediaSequence _sequence;
    private int _busy;
    private string? _shownPath;
    private bool _shownSomething;
    private bool _warnedMissingPicture;
    private readonly Dictionary<string, DateTime> _bad = new(StringComparer.OrdinalIgnoreCase);

    public PictureController(LooppaperConfig config, ILog log, WallpaperWindow window, IntPtr messageWindow, DesktopHost host, FrameCache frame)
    {
        _config = config;
        _log = log;
        _window = window;
        _messageWindow = messageWindow;
        _host = host;
        _frame = frame;
        _scanner = new MediaScanner(log);
        _sequence = new MediaSequence(config.Shuffle);
    }

    /// <summary>Raised on the UI thread after a new frame has been swapped in.</summary>
    public event Action? FrameShown;

    private bool IsSlideshow => _config.Mode == WallpaperMode.Slideshow;

    public void Start()
    {
        if (IsSlideshow)
        {
            var ms = (uint)Math.Min(_config.IntervalSeconds * 1000L, uint.MaxValue);
            User32.SetTimer(_messageWindow, TimerId, ms, IntPtr.Zero);
            _log.Info($"Slideshow every {_config.IntervalSeconds}s{(_config.Shuffle ? ", shuffled" : string.Empty)}");
        }

        Advance();
    }

    public void Stop()
    {
        User32.KillTimer(_messageWindow, TimerId);
    }

    /// <summary>Handles the controller's window messages. Returns true when handled.</summary>
    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case User32.WM_TIMER when (nuint)wParam == TimerId:
                Advance();
                return true;
            case WM_FRAME_READY when (int)wParam != _generation:
                Gdi32.DeleteObject(lParam); // rendered for a controller that has since been replaced
                return true;
            case WM_FRAME_READY:
                OnFrameReady(lParam);
                return true;
            default:
                return false;
        }
    }

    private void Advance()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            return; // previous frame still rendering
        }

        var bounds = _host.ChildBounds();
        var monitors = Monitors.RelativeToVirtualScreen();
        var hwnd = _messageWindow;
        var generation = _generation;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var hbitmap = IntPtr.Zero;
            try
            {
                hbitmap = RenderNext(bounds.Width, bounds.Height, monitors);
            }
            catch (Exception ex)
            {
                _log.Error("Preparing the next picture failed", ex);
            }

            if (hbitmap == IntPtr.Zero || !User32.PostMessage(hwnd, WM_FRAME_READY, generation, hbitmap))
            {
                if (hbitmap != IntPtr.Zero)
                {
                    Gdi32.DeleteObject(hbitmap);
                }

                Volatile.Write(ref _busy, 0);
            }
        });
    }

    /// <summary>Runs on a thread-pool thread. Returns a rendered frame, or zero when nothing changed.</summary>
    private IntPtr RenderNext(int width, int height, IReadOnlyList<Core.Rendering.IntRect> monitors)
    {
        var background = _config.ResolveBackgroundColor();
        var pictures = _scanner.Scan(_config.ResolveCollectionFolder(), MediaKind.Picture, _config.MaxPictureSizeMb)
            .Where(p => !IsKnownBad(p))
            .ToList();
        var candidates = IsSlideshow ? SlideshowCandidates(pictures) : SingleCandidates(pictures);

        var tried = 0;
        foreach (var path in candidates)
        {
            if (_shownSomething && string.Equals(path, _shownPath, StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero; // e.g. a one-picture slideshow: keep the current frame
            }

            tried++;
            var hbitmap = PictureRenderer.Render(path, width, height, monitors, _config.Scaling, background, _log);
            if (hbitmap != IntPtr.Zero)
            {
                _shownPath = path;
                _log.Info($"Showing {path}");
                return hbitmap;
            }

            RememberBad(path);
        }

        if (_shownSomething && _shownPath is null)
        {
            return IntPtr.Zero; // background already showing
        }

        _log.Info(tried == 0 ? "No pictures to show; showing the background color" : "No picture could be decoded; showing the background color");
        _shownPath = null;
        return PictureRenderer.Render(null, width, height, monitors, _config.Scaling, background, _log);
    }

    /// <summary>Lazily yields the next pictures in slideshow order, at most one full pass.</summary>
    private IEnumerable<string> SlideshowCandidates(List<string> pictures)
    {
        _sequence.Update(pictures);
        for (var i = 0; i < _sequence.Count; i++)
        {
            yield return _sequence.Next()!;
        }
    }

    /// <summary>The configured picture first, then the collection as fallback.</summary>
    private IEnumerable<string> SingleCandidates(List<string> pictures)
    {
        var configured = ConfiguredPicture();
        if (configured is not null)
        {
            yield return configured;
        }

        foreach (var picture in pictures)
        {
            if (!string.Equals(picture, configured, StringComparison.OrdinalIgnoreCase))
            {
                yield return picture;
            }
        }
    }

    /// <summary>Undecodable files are skipped until they change on disk, so each is logged once.</summary>
    private bool IsKnownBad(string path) =>
        _bad.TryGetValue(path, out var stamp) && stamp == Stamp(path);

    private void RememberBad(string path) => _bad[path] = Stamp(path);

    private static DateTime Stamp(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
    private string? ConfiguredPicture()
    {
        var path = _config.ResolvePicture();
        if (path is null)
        {
            return null;
        }

        string? problem = null;
        if (!MediaScanner.IsSupported(path, MediaKind.Picture))
        {
            problem = "is not a .jpg/.jpeg/.png file";
        }
        else if (!File.Exists(path))
        {
            problem = "does not exist";
        }
        else if (new FileInfo(path).Length > _config.MaxPictureSizeMb * MediaScanner.BytesPerMb)
        {
            problem = $"is larger than {_config.MaxPictureSizeMb} MB";
        }

        if (problem is null)
        {
            return path;
        }

        if (!_warnedMissingPicture)
        {
            _warnedMissingPicture = true;
            _log.Warn($"Configured picture {path} {problem}; using the collection instead");
        }

        return null;
    }

    private void OnFrameReady(IntPtr hbitmap)
    {
        _frame.Swap(hbitmap);
        _shownSomething = true;
        Volatile.Write(ref _busy, 0);
        _window.Invalidate();

        if (!IsSlideshow)
        {
            // Single mode only keeps polling while there is nothing but background to show.
            if (_shownPath is null)
            {
                User32.SetTimer(_messageWindow, TimerId, EmptyRescanMs, IntPtr.Zero);
            }
            else
            {
                User32.KillTimer(_messageWindow, TimerId);
            }
        }

        FrameShown?.Invoke();
    }
}

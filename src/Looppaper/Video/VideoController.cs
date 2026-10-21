using System.Runtime.InteropServices;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Playback;
using Looppaper.Core.Rendering;
using Looppaper.Desktop;
using Looppaper.Native;

namespace Looppaper.Video;

/// <summary>
/// Plays one looping video as the wallpaper through the Media Foundation Media Engine, which
/// decodes on the GPU and presents straight into a child window, so Looppaper itself does no
/// per-frame work. Each monitor gets its own child window and engine; only the first plays sound.
/// </summary>
internal sealed unsafe class VideoController
{
    private const string ClassName = "LooppaperVideoWindow";

    private static bool _classRegistered;
    private static int _nextPlayerId;

    private readonly LooppaperConfig _config;
    private readonly ILog _log;
    private readonly WallpaperWindow _window;
    private readonly IntPtr _messageWindow;
    private readonly List<Player> _players = [];
    private bool _mfStarted;

    public VideoController(LooppaperConfig config, ILog log, WallpaperWindow window, IntPtr messageWindow)
    {
        _config = config;
        _log = log;
        _window = window;
        _messageWindow = messageWindow;
    }

    /// <summary>Raised on the UI thread when playback fails after it was started.</summary>
    public event Action? Failed;

    public string? Path { get; private set; }

    public bool Start(string path, IReadOnlyList<IntRect> monitors)
    {
        Path = path;
        var hr = MediaFoundation.MFStartup(MediaFoundation.MF_VERSION, MediaFoundation.MFSTARTUP_FULL);
        if (hr < 0)
        {
            _log.Error($"Media Foundation is not available (0x{hr:X8}); video wallpapers need it (Windows N editions: install the Media Feature Pack)");
            return false;
        }

        _mfStarted = true;
        if (!RegisterClass())
        {
            Stop();
            return false;
        }

        for (var i = 0; i < monitors.Count; i++)
        {
            var player = CreatePlayer(i, path, monitors[i], withAudio: i == 0);
            if (player is null)
            {
                Stop();
                return false;
            }

            _players.Add(player);
        }

        var volume = _config.Video.Volume;
        _log.Info($"Starting video {path} (looped, {monitors.Count} monitor(s), {(volume == 0 ? "muted" : $"volume {volume}")})");
        return true;
    }

    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != MediaEngineNotify.WM_VIDEO_EVENT)
        {
            return false;
        }

        var (id, hresult) = MediaEngineNotify.Unpack(lParam);
        var player = _players.FirstOrDefault(p => p.Id == id);
        if (player is null)
        {
            return true; // late event from a player that is already gone
        }

        switch ((uint)wParam)
        {
            case MediaFoundation.EVENT_LOADEDMETADATA:
                ApplyScaling(player);
                break;
            case MediaFoundation.EVENT_PLAYING:
                if (!player.LoggedPlaying)
                {
                    player.LoggedPlaying = true;
                    _log.Info($"Video playing on monitor {player.Monitor + 1}");
                }

                break;
            case MediaFoundation.EVENT_ERROR:
                _log.Error($"Video playback failed for {Path} (0x{hresult:X8})");
                Stop();
                Failed?.Invoke();
                break;
        }

        return true;
    }

    /// <summary>Pauses the players whose monitor cannot be seen and resumes the others. Paused video keeps its frame.</summary>
    public void ApplyPause(PauseState state)
    {
        foreach (var player in _players)
        {
            var paused = state.IsPaused(player.Bounds);
            if (paused == player.Paused || player.Engine == IntPtr.Zero)
            {
                continue;
            }

            var hr = paused ? MediaFoundation.Pause(player.Engine) : MediaFoundation.Play(player.Engine);
            if (hr < 0)
            {
                _log.Warn($"Video: {(paused ? "pausing" : "resuming")} monitor {player.Monitor + 1} failed (0x{hr:X8})");
                continue;
            }

            player.Paused = paused;
            _log.Info($"Video {(paused ? "paused" : "resumed")} on monitor {player.Monitor + 1}");
        }
    }

    public void Stop()
    {
        foreach (var player in _players)
        {
            if (player.Engine != IntPtr.Zero)
            {
                MediaFoundation.Shutdown(player.Engine);
            }

            MediaFoundation.Release(player.EngineEx);
            MediaFoundation.Release(player.Engine);
            if (player.Hwnd != IntPtr.Zero)
            {
                User32.DestroyWindow(player.Hwnd);
            }

            MediaEngineNotify.Free(player.Notify);
        }

        _players.Clear();
        if (_mfStarted)
        {
            MediaFoundation.MFShutdown();
            _mfStarted = false;
        }
    }

    private Player? CreatePlayer(int monitor, string path, IntRect bounds, bool withAudio)
    {
        var player = new Player { Id = Interlocked.Increment(ref _nextPlayerId), Monitor = monitor, Bounds = bounds };
        player.Hwnd = User32.CreateWindowEx(
            0,
            ClassName,
            null,
            User32.WS_CHILD | User32.WS_VISIBLE | User32.WS_CLIPSIBLINGS,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            _window.Handle,
            IntPtr.Zero,
            Kernel32.GetModuleHandle(null),
            IntPtr.Zero);
        if (player.Hwnd == IntPtr.Zero)
        {
            _log.Error($"Creating the video window failed ({Marshal.GetLastPInvokeError()})");
            return null;
        }

        player.Notify = MediaEngineNotify.Create(_messageWindow, player.Id);
        var factory = IntPtr.Zero;
        var attributes = IntPtr.Zero;
        try
        {
            Check(MediaFoundation.CoCreateInstance(
                MediaFoundation.CLSID_MFMediaEngineClassFactory, IntPtr.Zero, MediaFoundation.CLSCTX_INPROC_SERVER,
                MediaFoundation.IID_IMFMediaEngineClassFactory, out factory), "creating the media engine factory");
            Check(MediaFoundation.MFCreateAttributes(out attributes, 2), "creating attributes");
            Check(MediaFoundation.SetUnknown(attributes, MediaFoundation.MF_MEDIA_ENGINE_CALLBACK, player.Notify), "setting the callback");
            Check(MediaFoundation.SetUINT64(attributes, MediaFoundation.MF_MEDIA_ENGINE_PLAYBACK_HWND, (ulong)player.Hwnd), "setting the window");
            Check(MediaFoundation.CreateInstance(factory, 0, attributes, out player.Engine), "creating the media engine");
            Check(MediaFoundation.QueryInterface(player.Engine, MediaFoundation.IID_IMFMediaEngineEx, out player.EngineEx), "querying IMFMediaEngineEx");

            var volume = withAudio ? _config.Video.Volume : 0;
            Check(MediaFoundation.SetLoop(player.Engine, true), "enabling loop");
            Check(MediaFoundation.SetMuted(player.Engine, volume == 0), "setting mute");
            Check(MediaFoundation.SetVolume(player.Engine, volume / 100.0), "setting the volume");
            Check(MediaFoundation.SetSource(player.Engine, path), "opening the video");
            Check(MediaFoundation.Play(player.Engine), "starting playback");
            return player;
        }
        catch (COMException ex)
        {
            _log.Error($"Video: {ex.Message}");
            _players.Add(player); // so Stop() cleans it up
            return null;
        }
        finally
        {
            MediaFoundation.Release(attributes);
            MediaFoundation.Release(factory);
        }
    }

    private void ApplyScaling(Player player)
    {
        if (MediaFoundation.GetNativeVideoSize(player.Engine, out var width, out var height) < 0 || width == 0 || height == 0)
        {
            return;
        }

        // The child window covers exactly one monitor, so compute in its client coordinates.
        var client = new IntRect(0, 0, player.Bounds.Width, player.Bounds.Height);
        var destination = ScalingMath.Destination((int)width, (int)height, client, _config.Scaling);
        var (source, visible) = VideoViewport.Visible(destination, client);
        var background = _config.ResolveBackgroundColor();
        var hr = MediaFoundation.UpdateVideoStream(
            player.EngineEx,
            new MFVideoNormalizedRect { Left = source.Left, Top = source.Top, Right = source.Right, Bottom = source.Bottom },
            new RECT(visible.X, visible.Y, visible.Right, visible.Bottom),
            new MFARGB { Red = background.R, Green = background.G, Blue = background.B, Alpha = 255 });
        if (hr < 0)
        {
            _log.Warn($"Video: applying '{_config.Scaling}' scaling failed (0x{hr:X8})");
        }
        else
        {
            _log.Info($"Video is {width}x{height}; monitor {player.Monitor + 1} shows {visible} ({_config.Scaling})");
        }
    }

    private static void Check(int hresult, string what)
    {
        if (hresult < 0)
        {
            throw new COMException($"{what} failed (0x{hresult:X8})", hresult);
        }
    }

    private bool RegisterClass()
    {
        if (_classRegistered)
        {
            return true;
        }

        var color = _config.ResolveBackgroundColor();
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = Kernel32.GetModuleHandle(null),
                hbrBackground = Gdi32.CreateSolidBrush(color.ToColorRef()),
                lpszClassName = name,
            };
            if (User32.RegisterClassEx(wc) == 0)
            {
                _log.Error($"Registering the video window class failed ({Marshal.GetLastPInvokeError()})");
                return false;
            }
        }

        _classRegistered = true;
        return true;
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        User32.DefWindowProc(hwnd, msg, wParam, lParam);

    private sealed class Player
    {
        public int Id;
        public int Monitor;
        public IntRect Bounds;
        public IntPtr Hwnd;
        public IntPtr Notify;
        public IntPtr Engine;
        public IntPtr EngineEx;
        public bool LoggedPlaying;
        public bool Paused;
    }
}

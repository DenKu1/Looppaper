using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Media;
using Looppaper.Native;

namespace Looppaper.Audio;

/// <summary>
/// Plays the music folder as an endless playlist through the Windows MCI "mpegvideo" device
/// (built into Windows, decodes in the system's MP3 codec, no extra dependencies). MCI calls run on
/// the UI thread and end-of-track arrives as MM_MCINOTIFY on the wallpaper window.
/// </summary>
internal sealed class MusicPlayer
{
    private const string Alias = "looppaper_music";
    private const nuint RescanTimerId = 2;
    private const uint RescanMs = 60_000;

    private readonly MusicConfig _music;
    private readonly string _folder;
    private readonly ILog _log;
    private readonly IntPtr _hwnd;
    private readonly MediaScanner _scanner;
    private readonly MediaSequence _sequence;
    private bool _open;
    private int _consecutiveFailures;
    private bool _stopped;

    public MusicPlayer(LooppaperConfig config, IntPtr hwnd, ILog log)
    {
        _music = config.Music;
        _folder = config.ResolveMusicFolder();
        _hwnd = hwnd;
        _log = log;
        _scanner = new MediaScanner(log);
        _sequence = new MediaSequence(_music.Shuffle);
    }

    public void Start()
    {
        _log.Info($"Music from {_folder}, volume {_music.Volume}{(_music.Shuffle ? ", shuffled" : string.Empty)}, max {_music.MaxFileSizeMb} MB per file");
        PlayNext();
    }

    public void Stop()
    {
        _stopped = true;
        User32.KillTimer(_hwnd, RescanTimerId);
        Close();
    }

    public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WinMm.MM_MCINOTIFY:
                OnNotify((int)wParam);
                return true;
            case User32.WM_TIMER when (nuint)wParam == RescanTimerId:
                User32.KillTimer(_hwnd, RescanTimerId);
                PlayNext();
                return true;
            default:
                return false;
        }
    }

    private void OnNotify(int flags)
    {
        if (_stopped)
        {
            return;
        }

        switch (flags)
        {
            case WinMm.MCI_NOTIFY_SUCCESSFUL:
                _consecutiveFailures = 0;
                PlayNext();
                break;
            case WinMm.MCI_NOTIFY_FAILURE:
                _log.Warn("Playback of the current track failed");
                _consecutiveFailures++;
                PlayNext();
                break;

            // SUPERSEDED / ABORTED come from our own close or replay; nothing to do.
        }
    }

    private void PlayNext()
    {
        Close();
        if (_stopped)
        {
            return;
        }

        _sequence.Update(_scanner.Scan(_folder, MediaKind.Music, _music.MaxFileSizeMb));
        if (_sequence.Count == 0 || _consecutiveFailures >= Math.Max(1, _sequence.Count))
        {
            if (_sequence.Count > 0)
            {
                _log.Warn("No track could be played; trying again in a minute");
            }

            _consecutiveFailures = 0;
            User32.SetTimer(_hwnd, RescanTimerId, RescanMs, IntPtr.Zero);
            return;
        }

        var path = _sequence.Next()!;
        var error = WinMm.Send($"open \"{path}\" type mpegvideo alias {Alias}");
        if (error is null)
        {
            _open = true;
            var mciVolume = _music.Volume * 10; // MCI range is 0..1000
            if (WinMm.Send($"setaudio {Alias} volume to {mciVolume}") is { } volumeError)
            {
                _log.Warn($"Setting the volume failed: {volumeError}");
            }

            error = WinMm.Send($"play {Alias} notify", _hwnd);
        }

        if (error is not null)
        {
            _log.Warn($"Cannot play {path}: {error}");
            _consecutiveFailures++;
            Close();

            // Retry from the message loop rather than recursing.
            User32.SetTimer(_hwnd, RescanTimerId, 1000, IntPtr.Zero);
            return;
        }

        _log.Info($"Playing {path}");
    }


    private void Close()
    {
        if (_open)
        {
            WinMm.Send($"close {Alias}");
            _open = false;
        }
    }
}

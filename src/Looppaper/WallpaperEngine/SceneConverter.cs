// Ported from wp2video (https://github.com/unio207/wp2video), MIT License — see THIRD-PARTY-NOTICES.md
using System.Diagnostics;
using Looppaper.Core.Logging;
using Looppaper.Core.Tools;
using Looppaper.Core.WallpaperEngine;
using Looppaper.Native;

namespace Looppaper.WallpaperEngine;

/// <summary>
/// Records a scene or web wallpaper to a looping .mp4: Wallpaper Engine plays it in a window of its
/// own (<c>-control openWallpaper -playInWindow</c>), the window is moved off-screen, ffmpeg 8+
/// records it with <c>gfxcapture</c>, and the window is closed again. The user's desktop wallpaper is
/// not touched.
/// </summary>
internal sealed class SceneConverter
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly FfmpegTool _ffmpeg;
    private readonly string _wallpaperExe;
    private readonly ILog _log;
    private readonly Action<string> _status;
    private readonly Dictionary<string, bool> _encoderWorks = new(StringComparer.Ordinal);

    public SceneConverter(FfmpegTool ffmpeg, string wallpaperExe, ILog log, Action<string> status)
    {
        _ffmpeg = ffmpeg;
        _wallpaperExe = wallpaperExe;
        _log = log;
        _status = status;
    }

    /// <summary>The loop length of the item, using ffprobe for a web page's video.</summary>
    public LoopLength? FindLoop(WorkshopItem item) =>
        SceneFiles.LoopFor(item, video => MediaProbe.Probe(_ffmpeg, video)?.Duration);

    /// <summary>Records the item into <paramref name="output"/>; throws <see cref="ConversionException"/> on failure.</summary>
    public async Task ConvertAsync(WorkshopItem item, CapturePlan plan, string output, IProgress<FfmpegProgress> progress, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        var recording = Path.Combine(directory, Path.GetFileNameWithoutExtension(output) + ".rec.mkv");
        var partial = Path.Combine(directory, Path.GetFileNameWithoutExtension(output) + ".part.mp4");

        var sceneSize = item.Type == WorkshopItemType.Scene ? SceneFiles.SceneSize(SceneFiles.ReadSceneJson(item.Folder)) : null;
        var position = SceneFiles.UserPosition(ReadWallpaperEngineConfig(), item.Folder);
        var geometry = CaptureGeometry.For(User32.GetSystemMetrics(User32.SM_CXSCREEN), User32.GetSystemMetrics(User32.SM_CYSCREEN), sceneSize, position);
        _log.Info($"Converting Workshop item {item.Id}: {plan.Seconds:0.###} s, seamless {plan.Seamless}, output {geometry.Width}x{geometry.Height}, render {geometry.RenderWidth}x{geometry.RenderHeight}{geometry.Crop}");

        int fps;
        string encoder;
        long bitrate;
        try
        {
            (fps, encoder, bitrate) = await RecordAsync(item, plan, geometry, recording, progress, cancellationToken).ConfigureAwait(false);

            _status(plan.Seamless ? "Making it loop seamlessly..." : "Saving...");
            var finish = plan.Seamless
                ? await FfmpegRunner.RunAsync(_ffmpeg, SceneCapture.SeamlessArgs(recording, plan.Seconds, encoder, bitrate, partial), TimeSpan.FromSeconds(plan.Seconds), progress, cancellationToken).ConfigureAwait(false)
                : await FfmpegRunner.RunAsync(_ffmpeg, SceneCapture.RemuxArgs(recording, encoder, partial), null, null, cancellationToken).ConfigureAwait(false);
            ThrowIfFailed(finish, "Saving the video");

            // The bitrate is capped, so this is rare: re-encode smaller once if the file came out too big.
            var size = new FileInfo(partial).Length;
            if (size > SceneCapture.MaxBytes)
            {
                var smaller = (long)(bitrate * 0.75 * SceneCapture.TargetBytes / size);
                _status($"The video came out at {WorkshopLibrary.SizeText(size)}; making it smaller...");
                var shrunk = partial + ".small.mp4";
                var shrink = await FfmpegRunner.RunAsync(_ffmpeg, SceneCapture.ShrinkArgs(partial, encoder, smaller, shrunk), TimeSpan.FromSeconds(plan.Seconds), progress, cancellationToken).ConfigureAwait(false);
                ThrowIfFailed(shrink, "Making the video smaller");
                File.Move(shrunk, partial, overwrite: true);
                if (new FileInfo(partial).Length is var still && still > SceneCapture.MaxBytes)
                {
                    _log.Warn($"Converted Workshop item {item.Id} is still {WorkshopLibrary.SizeText(still)}; keeping it with a size warning");
                    _status(Core.Compression.CompressionMessages.LargeWarning(Path.GetFileName(output), still));
                }
            }

            File.Move(partial, output, overwrite: true);
            _log.Info($"Converted Workshop item {item.Id} to {output} ({new FileInfo(output).Length} bytes, {fps} fps, {encoder}, {bitrate} bit/s)");
        }
        finally
        {
            TryDelete(recording);
            TryDelete(partial);
            TryDelete(partial + ".small.mp4");
        }
    }

    private async Task<(int Fps, string Encoder, long Bitrate)> RecordAsync(
        WorkshopItem item, CapturePlan plan, CaptureGeometry geometry, string recording, IProgress<FfmpegProgress> progress, CancellationToken cancellationToken)
    {
        var startedAt = EnsureWallpaperEngineRunning();
        var title = $"looppaper-{Environment.ProcessId}";
        var offScreenX = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN) - geometry.RenderWidth - 100;

        _status("Opening the wallpaper in Wallpaper Engine...");
        RunWallpaperEngine(
            "-control", "openWallpaper", "-file", item.ProjectFilePath, "-playInWindow", title,
            "-width", geometry.RenderWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-height", geometry.RenderHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-x", offScreenX.ToString(System.Globalization.CultureInfo.InvariantCulture), "-y", "0");
        try
        {
            var hwnd = IntPtr.Zero;
            for (var i = 0; i < 80 && hwnd == IntPtr.Zero; i++)
            {
                hwnd = User32.FindWindow(null, title);
                if (hwnd == IntPtr.Zero)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
            }

            if (hwnd == IntPtr.Zero)
            {
                throw new ConversionException("Wallpaper Engine didn't open the wallpaper window.");
            }

            Kernel32.SetThreadExecutionState(Kernel32.ES_CONTINUOUS | Kernel32.ES_SYSTEM_REQUIRED | Kernel32.ES_DISPLAY_REQUIRED);
            var handle = hwnd.ToInt64();

            // Off-screen, so the user's desktop is untouched.
            PlaceWindow(hwnd, offScreenX, 0, geometry.RenderWidth, geometry.RenderHeight);
            _status("Loading the wallpaper...");
            if (!await WaitUntilDrawnAsync(handle, cancellationToken).ConfigureAwait(false))
            {
                // Some systems don't render off-screen windows; fall back to on-screen.
                _log.Warn("The off-screen Wallpaper Engine window stayed black; moving it on-screen");
                PlaceWindow(hwnd, 0, 0, geometry.RenderWidth, geometry.RenderHeight);
                if (!await WaitUntilDrawnAsync(handle, cancellationToken).ConfigureAwait(false))
                {
                    throw new ConversionException("Wallpaper Engine showed only black for 14 s, so nothing was saved. Check that the wallpaper plays in Wallpaper Engine and try again.");
                }
            }

            _status("Testing the frame rate...");
            var measured = MeasureFps(handle);
            if (measured < 2)
            {
                PlaceWindow(hwnd, 0, 0, geometry.RenderWidth, geometry.RenderHeight);
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                measured = MeasureFps(handle);
            }

            if (measured < 2)
            {
                throw new ConversionException("Couldn't capture the wallpaper window.");
            }

            var fps = SceneCapture.ChooseFps(measured);
            var encoder = SceneCapture.PickEncoder(SceneCapture.Codec(geometry.Width, geometry.Height, fps), EncoderWorks);
            var bitrate = SceneCapture.Bitrate(plan.Seconds, geometry.Width, geometry.Height, fps);
            var note = fps < SceneCapture.TargetFps ? $" (your PC rendered ~{measured:0} fps)" : string.Empty;
            _status($"Recording {plan.RecordSeconds:0.#} s at {geometry.Width}x{geometry.Height}, {fps} fps{note}, with {encoder}...");
            var record = await FfmpegRunner.RunAsync(
                _ffmpeg,
                SceneCapture.RecordArgs(handle, geometry, fps, encoder, bitrate, plan.RecordSeconds, recording),
                TimeSpan.FromSeconds(plan.RecordSeconds),
                progress,
                cancellationToken).ConfigureAwait(false);
            ThrowIfFailed(record, "Recording");
            return (fps, encoder, bitrate);
        }
        finally
        {
            try
            {
                RunWallpaperEngine("-control", "closeWallpaper", "-location", title);
            }
            catch (ConversionException ex)
            {
                _log.Warn($"Closing the Wallpaper Engine window failed: {ex.Message}");
            }

            Kernel32.SetThreadExecutionState(Kernel32.ES_CONTINUOUS);
            if (startedAt is { } started)
            {
                StopWallpaperEngine(started);
            }
        }
    }

    private bool EncoderWorks(string encoder)
    {
        if (!_encoderWorks.TryGetValue(encoder, out var works))
        {
            works = FfmpegProcess.Run(_ffmpeg.FfmpegPath, ["-hide_banner", .. SceneCapture.ProbeEncoderArgs(encoder)], ProbeTimeout).Succeeded;
            _encoderWorks[encoder] = works;
            _log.Info($"Encoder {encoder}: {(works ? "works" : "not available")}");
        }

        return works;
    }

    /// <summary>True once the window shows something other than black; false if still black after 7 seconds.</summary>
    private async Task<bool> WaitUntilDrawnAsync(long hwnd, CancellationToken cancellationToken)
    {
        var end = DateTime.UtcNow.AddSeconds(7);
        while (DateTime.UtcNow < end)
        {
            // A window that renders nothing delivers no frame at all, so the grab needs a timeout.
            var grab = FfmpegProcess.Run(_ffmpeg.FfmpegPath, SceneCapture.ProbeDrawnArgs(hwnd), TimeSpan.FromSeconds(5));
            if (SceneCapture.IsDrawn(grab.StandardOutput))
            {
                return true;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private double MeasureFps(long hwnd)
    {
        var run = FfmpegProcess.Run(_ffmpeg.FfmpegPath, SceneCapture.MeasureFpsArgs(hwnd, SceneCapture.TargetFps), TimeSpan.FromSeconds(20));
        var fps = SceneCapture.ParseMeasuredFps(run.StandardError);
        _log.Info($"Wallpaper Engine window renders ~{fps:0.#} fps");
        return fps;
    }

    /// <summary>Sets the window's client area; SWP_NOSENDCHANGING lets it be larger than the screen.</summary>
    private static void PlaceWindow(IntPtr hwnd, int x, int y, int width, int height)
    {
        var rect = new RECT(0, 0, width, height);
        User32.AdjustWindowRectEx(ref rect, (uint)User32.GetWindowLong(hwnd, User32.GWL_STYLE), false, (uint)User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE));
        User32.SetWindowPos(hwnd, IntPtr.Zero, x, y, rect.Width, rect.Height, User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_NOSENDCHANGING);
    }

    /// <summary>Starts Wallpaper Engine if it isn't running. Returns when we started it, or null if it was already running.</summary>
    private DateTime? EnsureWallpaperEngineRunning()
    {
        var running = Process.GetProcessesByName("wallpaper64");
        var isRunning = running.Length > 0;
        foreach (var process in running)
        {
            process.Dispose();
        }

        if (isRunning)
        {
            return null;
        }

        _status("Starting Wallpaper Engine...");
        var startedAt = DateTime.Now.AddSeconds(-1);
        using (Process.Start(new ProcessStartInfo(_wallpaperExe) { UseShellExecute = false }))
        {
        }

        Thread.Sleep(8000);
        return startedAt;
    }

    /// <summary>Closes the Wallpaper Engine we started for the recording, so it is left as the user had it (not running).</summary>
    private void StopWallpaperEngine(DateTime startedAt)
    {
        foreach (var process in Process.GetProcessesByName("wallpaper64"))
        {
            using (process)
            {
                try
                {
                    if (process.StartTime < startedAt)
                    {
                        continue; // the user started this one meanwhile
                    }

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    _log.Info("Closed the Wallpaper Engine instance started for the conversion");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    _log.Warn($"Closing Wallpaper Engine failed: {ex.Message}");
                }
            }
        }
    }

    private void RunWallpaperEngine(params string[] arguments)
    {
        var start = new ProcessStartInfo(_wallpaperExe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is not null && !process.WaitForExit(30_000))
            {
                _log.Warn($"wallpaper64.exe {arguments[1]} did not return within 30 s");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ConversionException($"Could not run {_wallpaperExe}: {ex.Message}");
        }
    }

    private string? ReadWallpaperEngineConfig()
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(_wallpaperExe)!, "config.json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ThrowIfFailed(FfmpegResult result, string step)
    {
        if (result.Cancelled)
        {
            throw new OperationCanceledException();
        }

        if (!result.Succeeded)
        {
            _log.Error($"{step} failed (ffmpeg exit {result.ExitCode}): {result.ErrorSummary}");
            throw new ConversionException($"{step} failed (ffmpeg exit code {result.ExitCode}): {result.ErrorSummary}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A conversion step failed; the message is meant for the person at the console.</summary>
internal sealed class ConversionException(string message) : Exception(message);

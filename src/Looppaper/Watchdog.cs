using System.Diagnostics;
using Looppaper.Core.Logging;
using Looppaper.Core.Watchdog;
using Looppaper.Desktop;
using Looppaper.Native;

namespace Looppaper;

/// <summary>
/// A second, idle copy of Looppaper (<c>--watchdog &lt;pid&gt;</c>) that waits for the wallpaper process to end.
/// If it ends without a clean stop the Windows wallpaper is put back (our window is gone, and Explorer may
/// otherwise keep showing its last frame); after a crash Looppaper is started again (see <see cref="CrashPolicy"/>).
/// </summary>
internal static class Watchdog
{
    private const int SM_SHUTTINGDOWN = 0x2000;

    /// <summary>Called by the wallpaper process: starts its watchdog. Never throws.</summary>
    public static void Start(string[] args, ILog log)
    {
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.");
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            start.ArgumentList.Add("--watchdog");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var watchdog = Process.Start(start);
            log.Info($"Watchdog started (pid {watchdog?.Id})");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Warn($"Starting the watchdog failed: {ex.Message}");
        }
    }

    /// <summary>Runs as the watchdog until the watched wallpaper stops for good.</summary>
    public static int Run(int pid, string[] args, ILog log)
    {
        ResourceSaver.Apply(log);
        WallpaperRestorer.Capture(log);
        var policy = new CrashPolicy();
        while (true)
        {
            Process watched;
            try
            {
                watched = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                log.Info($"Watchdog: process {pid} is not running; nothing to watch");
                return 0;
            }

            int exitCode;
            using (watched)
            {
                log.Info($"Watchdog: watching Looppaper (pid {pid})");
                _ = watched.SafeHandle; // keep a handle open now so the exit code can still be read after it ends
                ResourceSaver.Trim();
                watched.WaitForExit();
                try
                {
                    exitCode = watched.ExitCode;
                }
                catch (InvalidOperationException)
                {
                    exitCode = -1;
                }
            }

            var action = policy.Decide(exitCode, DateTime.UtcNow);
            if (action == WatchdogAction.Exit)
            {
                log.Info($"Watchdog: Looppaper stopped normally (exit code {exitCode})");
                return 0;
            }

            log.Warn($"Watchdog: Looppaper (pid {pid}) ended unexpectedly with exit code 0x{exitCode:X8}; restoring the Windows wallpaper");
            WallpaperRestorer.Reapply(log);
            if (action == WatchdogAction.Restore || User32.GetSystemMetrics(SM_SHUTTINGDOWN) != 0)
            {
                if (CrashPolicy.IsCrash(exitCode))
                {
                    log.Warn($"Watchdog: not restarting Looppaper; it crashed {CrashPolicy.MaxRestarts} times within {CrashPolicy.RestartWindow.TotalMinutes:0} minutes (or Windows is shutting down)");
                }

                return 0;
            }

            // Give Explorer a moment to repaint before attaching again.
            Thread.Sleep(TimeSpan.FromSeconds(2));
            var restarted = Restart(args, log);
            if (restarted is null)
            {
                return 1;
            }

            pid = restarted.Value;
        }
    }

    private static int? Restart(string[] args, ILog log)
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in CrashPolicy.RestartArguments(args))
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Process.Start returned nothing.");
            log.Info($"Watchdog: started Looppaper again (pid {process.Id})");
            return process.Id;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Error("Watchdog: starting Looppaper again failed", ex);
            return null;
        }
    }
}

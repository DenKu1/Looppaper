namespace Looppaper.Core.Watchdog;

public enum WatchdogAction
{
    /// <summary>A clean stop (or a start that never ran the wallpaper): nothing to do.</summary>
    Exit,

    /// <summary>Ended some other way (killed, failed): put the Windows wallpaper back and stop watching.</summary>
    Restore,

    /// <summary>Crashed: put the Windows wallpaper back and start Looppaper again.</summary>
    RestoreAndRestart,
}

/// <summary>What the watchdog does when the wallpaper process ends, and how often it may start it again.</summary>
public sealed class CrashPolicy
{
    public const int MaxRestarts = 3;

    public static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(10);

    /// <summary>Exit codes of a stop that needs nothing: clean exit, bad command line, already running.</summary>
    private static readonly int[] QuietExitCodes = [0, 2, 3];

    private const uint ClrUnhandledException = 0xE0434352;
    private const uint ClrFatalError = 0x80131506;

    private readonly Queue<DateTime> _restarts = new();

    /// <summary>
    /// True for exit codes that mean a crash: NTSTATUS errors (0xC0000005 access violation, 0xC0000409 fail-fast, ...)
    /// and the .NET runtime's unhandled-exception and fatal-error codes. Task Manager and <c>taskkill /f</c> end a
    /// process with 1 and <c>Stop-Process</c> with -1 (0xFFFFFFFF); those are a person's choice, not crashes.
    /// </summary>
    public static bool IsCrash(int exitCode)
    {
        var code = unchecked((uint)exitCode);
        return code != 0xFFFFFFFF && ((code & 0xF0000000) == 0xC0000000 || code is ClrUnhandledException or ClrFatalError);
    }

    public WatchdogAction Decide(int exitCode, DateTime now)
    {
        if (QuietExitCodes.Contains(exitCode))
        {
            return WatchdogAction.Exit;
        }

        if (!IsCrash(exitCode))
        {
            return WatchdogAction.Restore;
        }

        while (_restarts.Count > 0 && now - _restarts.Peek() > RestartWindow)
        {
            _restarts.Dequeue();
        }

        if (_restarts.Count >= MaxRestarts)
        {
            return WatchdogAction.Restore;
        }

        _restarts.Enqueue(now);
        return WatchdogAction.RestoreAndRestart;
    }

    /// <summary>
    /// The arguments to start the wallpaper again with: the watchdog's own arguments without <c>--watchdog</c>,
    /// <c>--background</c> and <c>--watched</c>, plus <c>--background --watched</c> (the watchdog keeps watching it).
    /// </summary>
    public static IReadOnlyList<string> RestartArguments(IReadOnlyList<string> watchdogArgs)
    {
        var result = new List<string>();
        for (var i = 0; i < watchdogArgs.Count; i++)
        {
            var arg = watchdogArgs[i];
            if (string.Equals(arg, "--watchdog", StringComparison.OrdinalIgnoreCase))
            {
                i++; // and its value
                continue;
            }

            if (arg.StartsWith("--watchdog=", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--watched", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(arg);
        }

        result.Add("--background");
        result.Add("--watched");
        return result;
    }
}

using Looppaper.Core.Playback;

namespace Looppaper.Core.Control;

/// <summary>A command sent from a <c>Looppaper.exe --pause</c> (etc.) process to the running wallpaper.</summary>
public enum ControlCommand
{
    Status = 1,
    Pause = 2,
    Resume = 3,
    Next = 4,
    Previous = 5,
}

/// <summary>What the running wallpaper is doing; the reply to every <see cref="ControlCommand"/>.</summary>
[Flags]
public enum ControlState
{
    None = 0,

    /// <summary>Always set by a running instance, so a reply can be told apart from a failed send (0).</summary>
    Running = 1,

    Video = 2,

    Pictures = 4,

    /// <summary>The pictures change on a timer (slideshow mode).</summary>
    Slideshow = 8,

    /// <summary>Paused by <c>--pause</c> (or the tray icon) until <c>--resume</c>.</summary>
    PausedByUser = 16,

    /// <summary>A full-screen window covers at least one monitor, so that monitor is paused.</summary>
    MonitorCovered = 32,

    /// <summary>Explorer's desktop is gone; waiting for it to come back.</summary>
    WaitingForDesktop = 64,

    /// <summary>The command does not apply to what is shown (e.g. <c>--next</c> while a video plays).</summary>
    NotApplicable = 128,
}

/// <summary>
/// The reply packed into the window message result: <see cref="ControlState"/> in the low 16 bits and the
/// automatic <see cref="PauseReason"/>s (not <see cref="PauseReason.User"/>) in the high bits.
/// </summary>
public readonly record struct ControlReply(ControlState State, PauseReason AutoPause)
{
    /// <summary>Name passed to <c>RegisterWindowMessage</c> for the command message (command in wParam).</summary>
    public const string MessageName = "Looppaper.Control.v1";

    public long Encode() => ((long)State & 0xFFFF) |((long)(AutoPause & ~PauseReason.User) << 16);

    public static ControlReply Decode(long value) =>
        new((ControlState)(value & 0xFFFF), (PauseReason)(value >> 16) & ~PauseReason.User);

    public bool IsPaused => State.HasFlag(ControlState.PausedByUser) || AutoPause != PauseReason.None;

    /// <summary>One line describing the state, e.g. "Showing a video; paused (you paused it)."</summary>
    public string Describe()
    {
        if (State.HasFlag(ControlState.WaitingForDesktop))
        {
            return "Waiting for the Windows desktop to come back (Explorer restarted)" + PauseSuffix() + ".";
        }

        var shown = State.HasFlag(ControlState.Video) ? "Playing a video"
            : State.HasFlag(ControlState.Slideshow) ? "Showing a slideshow"
            : State.HasFlag(ControlState.Pictures) ? "Showing one picture"
            : "Running";
        if (!IsPaused && State.HasFlag(ControlState.MonitorCovered))
        {
            return shown + "; paused on the monitor a full-screen window covers.";
        }

        return shown + PauseSuffix() + ".";
    }

    /// <summary>The line a command prints after it reached the running instance.</summary>
    public string DescribeResult(ControlCommand command)
    {
        var state = Describe();
        return command switch
        {
            ControlCommand.Pause => (State.HasFlag(ControlState.PausedByUser) ? "Paused until you run --resume. " : string.Empty) + state,
            ControlCommand.Resume when IsPaused => "Resumed, but it is still paused automatically. " + state,
            ControlCommand.Resume => "Resumed. " + state,
            ControlCommand.Next or ControlCommand.Previous when State.HasFlag(ControlState.NotApplicable) =>
                (State.HasFlag(ControlState.Video) ? "A video is the wallpaper, so there is no other picture to switch to. "
                    : State.HasFlag(ControlState.Slideshow) ? "There is no other picture to switch to. "
                    : State.HasFlag(ControlState.Pictures) ? "Single-picture mode shows one picture, so there is nothing to switch to. "
                    : "Nothing to switch to right now. ") + state,
            ControlCommand.Next => "Switching to the next picture. " + state,
            ControlCommand.Previous => "Switching to the previous picture. " + state,
            _ => state,
        };
    }

    private string PauseSuffix()
    {
        var why = new List<string>();
        if (State.HasFlag(ControlState.PausedByUser))
        {
            why.Add("you paused it; --resume continues");
        }

        if (AutoPause != PauseReason.None)
        {
            why.Add("automatically: " + DescribeReasons(AutoPause));
        }

        return why.Count == 0 ? string.Empty : "; paused (" + string.Join("; ", why) + ")";
    }

    private static string DescribeReasons(PauseReason reasons)
    {
        var names = new List<string>();
        foreach (var reason in Enum.GetValues<PauseReason>())
        {
            if (reason != PauseReason.None && reasons.HasFlag(reason))
            {
                names.Add(reason switch
                {
                    PauseReason.ExclusiveFullscreen => "a full-screen game or presentation",
                    PauseReason.Locked => "the PC is locked",
                    PauseReason.Disconnected => "the session is disconnected",
                    PauseReason.Suspended => "the PC is going to sleep",
                    PauseReason.DisplayOff => "the display is off",
                    PauseReason.BatterySaver => "battery saver is on",
                    PauseReason.OnBattery => "running on battery",
                    _ => reason.ToString(),
                });
            }
        }

        return string.Join(", ", names);
    }
}

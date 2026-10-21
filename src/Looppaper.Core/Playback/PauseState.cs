using Looppaper.Core.Rendering;

namespace Looppaper.Core.Playback;

/// <summary>Why everything is paused. Several can be active at once; playback resumes when none is left.</summary>
[Flags]
public enum PauseReason
{
    None = 0,

    /// <summary>Windows reports a full-screen Direct3D app (a game) or presentation mode.</summary>
    ExclusiveFullscreen = 1,

    /// <summary>The session is locked.</summary>
    Locked = 2,

    /// <summary>The session is disconnected (fast user switching, Remote Desktop).</summary>
    Disconnected = 4,

    /// <summary>The PC is going to sleep.</summary>
    Suspended = 8,

    /// <summary>The display is off.</summary>
    DisplayOff = 16,

    /// <summary>Battery saver (Windows 10) or Energy Saver (Windows 11) is on.</summary>
    BatterySaver = 32,

    /// <summary>Running on battery.</summary>
    OnBattery = 64,
}

/// <summary>
/// What should be paused right now: everything (for any <see cref="PauseReason"/>), or only the monitors a
/// full-screen window covers, whose wallpaper cannot be seen. Monitors are rectangles relative to the
/// virtual screen, as used for the video players.
/// </summary>
public sealed class PauseState
{
    private readonly List<IntRect> _covered = [];

    public PauseReason Reasons { get; private set; }

    public IReadOnlyList<IntRect> CoveredMonitors => _covered;

    /// <summary>Sets or clears one reason. Returns true when that changed anything.</summary>
    public bool Set(PauseReason reason, bool active)
    {
        var updated = active ? Reasons | reason : Reasons & ~reason;
        if (updated == Reasons)
        {
            return false;
        }

        Reasons = updated;
        return true;
    }

    /// <summary>Replaces the monitors covered by a full-screen window. Returns true when the set changed.</summary>
    public bool SetCovered(IEnumerable<IntRect> monitors)
    {
        var updated = monitors.Distinct().ToList();
        if (updated.Count == _covered.Count && updated.All(_covered.Contains))
        {
            return false;
        }

        _covered.Clear();
        _covered.AddRange(updated);
        return true;
    }

    /// <summary>Whether playback on this monitor should be paused.</summary>
    public bool IsPaused(IntRect monitor) => Reasons != PauseReason.None || _covered.Contains(monitor);

    /// <summary>Whether nothing on any of these monitors can be seen, e.g. to hold the slideshow.</summary>
    public bool IsPausedEverywhere(IReadOnlyCollection<IntRect> monitors) =>
        Reasons != PauseReason.None || (monitors.Count > 0 && monitors.All(_covered.Contains));

    public override string ToString()
    {
        var parts = new List<string>();
        if (Reasons != PauseReason.None)
        {
            parts.Add(Reasons.ToString());
        }

        if (_covered.Count > 0)
        {
            parts.Add("full-screen window on " + string.Join(", ", _covered));
        }

        return parts.Count == 0 ? "playing" : "paused: " + string.Join("; ", parts);
    }
}

public static class FullscreenRules
{
    /// <summary>
    /// True when a window covers a whole monitor, as borderless and exclusive full-screen apps and games do.
    /// Maximized windows leave the taskbar visible, so they do not count.
    /// </summary>
    public static bool Covers(IntRect window, IntRect monitor) =>
        monitor.Width > 0 && monitor.Height > 0 &&
        window.X <= monitor.X && window.Y <= monitor.Y && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
}

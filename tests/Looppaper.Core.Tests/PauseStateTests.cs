using Looppaper.Core.Configuration;
using Looppaper.Core.Playback;
using Looppaper.Core.Rendering;

namespace Looppaper.Core.Tests;

public class PauseStateTests
{
    private static readonly IntRect Left = new(0, 0, 2560, 1440);
    private static readonly IntRect Right = new(2560, 0, 1920, 1080);

    [Fact]
    public void NewState_PlaysEverywhere()
    {
        var state = new PauseState();

        Assert.False(state.IsPaused(Left));
        Assert.False(state.IsPausedEverywhere([Left, Right]));
        Assert.Equal("playing", state.ToString());
    }

    [Fact]
    public void Reason_PausesEveryMonitor_UntilCleared()
    {
        var state = new PauseState();

        Assert.True(state.Set(PauseReason.ExclusiveFullscreen, true));
        Assert.False(state.Set(PauseReason.ExclusiveFullscreen, true));
        Assert.True(state.IsPaused(Left));
        Assert.True(state.IsPaused(Right));
        Assert.True(state.IsPausedEverywhere([Left, Right]));

        Assert.True(state.Set(PauseReason.ExclusiveFullscreen, false));
        Assert.False(state.IsPaused(Left));
    }

    [Fact]
    public void CoveredMonitor_PausesOnlyThatMonitor()
    {
        var state = new PauseState();

        Assert.True(state.SetCovered([Right]));
        Assert.False(state.SetCovered([Right]));

        Assert.True(state.IsPaused(Right));
        Assert.False(state.IsPaused(Left));
        Assert.False(state.IsPausedEverywhere([Left, Right]));
        Assert.True(state.IsPausedEverywhere([Right]));
        Assert.Contains("full-screen window", state.ToString());

        Assert.True(state.SetCovered([]));
        Assert.False(state.IsPaused(Right));
    }

    [Fact]
    public void NoMonitors_IsNotPausedEverywhere()
    {
        Assert.False(new PauseState().IsPausedEverywhere([]));
    }

    [Theory]
    [InlineData(0, 0, 2560, 1440, true)] // borderless full screen
    [InlineData(-8, -8, 2576, 1456, true)] // slightly larger (window borders outside the monitor)
    [InlineData(-8, -8, 2576, 1408, false)] // maximized: the taskbar is still visible
    [InlineData(100, 100, 800, 600, false)] // ordinary window
    [InlineData(2560, 0, 1920, 1080, false)] // full screen on the other monitor
    public void Covers_OnlyWhenTheWholeMonitorIsHidden(int x, int y, int width, int height, bool expected)
    {
        Assert.Equal(expected, FullscreenRules.Covers(new IntRect(x, y, width, height), Left));
    }

    [Fact]
    public void Config_PauseDefaultsAndParsing()
    {
        var log = new MemoryLog();
        Assert.True(new LooppaperConfig().Pause.OnFullscreen);

        var config = ConfigLoader.Parse("""{ "pause": { "onFullscreen": false, "nope": 1 } }""", log);

        Assert.False(config.Pause.OnFullscreen);
        Assert.Contains(log.Warnings, w => w.Contains("pause.nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Config_LockAndPowerOptions()
    {
        var log = new MemoryLog();
        var defaults = new PauseConfig();
        Assert.True(defaults.OnLock);
        Assert.True(defaults.OnBatterySaver);
        Assert.False(defaults.OnBattery);

        var config = ConfigLoader.Parse("""{ "pause": { "onLock": false, "onBatterySaver": false, "onBattery": true } }""", log);

        Assert.Empty(log.Warnings);
        Assert.False(config.Pause.OnLock);
        Assert.False(config.Pause.OnBatterySaver);
        Assert.True(config.Pause.OnBattery);
    }

    [Theory]
    [InlineData(PauseReason.ExclusiveFullscreen, false)]
    [InlineData(PauseReason.Locked, false)]
    [InlineData(PauseReason.Disconnected, false)]
    [InlineData(PauseReason.BatterySaver, false)]
    [InlineData(PauseReason.OnBattery, false)]
    [InlineData(PauseReason.Suspended, true)]
    [InlineData(PauseReason.DisplayOff, true)]
    public void Allows_FollowsTheSwitches_SleepAndDisplayOffAlwaysPause(PauseReason reason, bool allowedWhenAllOff)
    {
        var off = new PauseConfig { OnFullscreen = false, OnLock = false, OnBatterySaver = false, OnBattery = false };
        var on = new PauseConfig { OnFullscreen = true, OnLock = true, OnBatterySaver = true, OnBattery = true };

        Assert.Equal(allowedWhenAllOff, off.Allows(reason));
        Assert.True(on.Allows(reason));
    }

    [Fact]
    public void SeveralReasons_ResumeOnlyWhenAllAreGone()
    {
        var state = new PauseState();
        state.Set(PauseReason.Locked, true);
        state.Set(PauseReason.DisplayOff, true);

        state.Set(PauseReason.DisplayOff, false);
        Assert.True(state.IsPaused(Left));
        Assert.Contains("Locked", state.ToString());

        state.Set(PauseReason.Locked, false);
        Assert.False(state.IsPaused(Left));
    }

    [Fact]
    public void Config_PauseMustBeAnObject()
    {
        var log = new MemoryLog();

        var config = ConfigLoader.Parse("""{ "pause": true }""", log);

        Assert.True(config.Pause.OnFullscreen);
        Assert.Contains(log.Warnings, w => w.Contains("'pause' must be an object", StringComparison.Ordinal));
    }
}

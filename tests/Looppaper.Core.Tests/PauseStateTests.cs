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
    public void Config_PauseMustBeAnObject()
    {
        var log = new MemoryLog();

        var config = ConfigLoader.Parse("""{ "pause": true }""", log);

        Assert.True(config.Pause.OnFullscreen);
        Assert.Contains(log.Warnings, w => w.Contains("'pause' must be an object", StringComparison.Ordinal));
    }
}

using Looppaper.Core.CommandLine;
using Looppaper.Core.Configuration;
using Looppaper.Core.Watchdog;

namespace Looppaper.Core.Tests;

public class CrashPolicyTests
{
    private static readonly DateTime Start = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)] // clean stop
    [InlineData(2)] // bad command line
    [InlineData(3)] // already running
    public void QuietExits_NeedNothing(int exitCode)
    {
        Assert.Equal(WatchdogAction.Exit, new CrashPolicy().Decide(exitCode, Start));
    }

    [Theory]
    [InlineData(1)] // Task Manager, taskkill /f
    [InlineData(-1)] // Stop-Process
    public void Killed_RestoresWithoutRestarting(int exitCode)
    {
        Assert.False(CrashPolicy.IsCrash(exitCode));
        Assert.Equal(WatchdogAction.Restore, new CrashPolicy().Decide(exitCode, Start));
    }

    [Theory]
    [InlineData(0xC0000005)] // access violation
    [InlineData(0xC0000409)] // fail fast
    [InlineData(0xC00000FD)] // stack overflow
    [InlineData(0xE0434352)] // unhandled .NET exception
    [InlineData(0x80131506)] // .NET fatal error
    public void Crashes_AreRecognised(uint exitCode)
    {
        Assert.True(CrashPolicy.IsCrash(unchecked((int)exitCode)));
    }

    [Fact]
    public void Crashes_RestartAtMostThreeTimesInTenMinutes()
    {
        var policy = new CrashPolicy();
        var crash = unchecked((int)0xC0000005);

        Assert.Equal(WatchdogAction.RestoreAndRestart, policy.Decide(crash, Start));
        Assert.Equal(WatchdogAction.RestoreAndRestart, policy.Decide(crash, Start.AddMinutes(1)));
        Assert.Equal(WatchdogAction.RestoreAndRestart, policy.Decide(crash, Start.AddMinutes(2)));
        Assert.Equal(WatchdogAction.Restore, policy.Decide(crash, Start.AddMinutes(3)));

        // Once the first restart is more than ten minutes old there is room again.
        Assert.Equal(WatchdogAction.RestoreAndRestart, policy.Decide(crash, Start.AddMinutes(10.5)));
    }

    [Fact]
    public void RestartArguments_KeepOverridesAndMarkTheCopyAsWatched()
    {
        var args = new[] { "--folder", @"C:\Walls", "--background", "--watchdog", "1234", "--shuffle" };

        Assert.Equal(["--folder", @"C:\Walls", "--shuffle", "--background", "--watched"], CrashPolicy.RestartArguments(args));
        Assert.Equal(["--background", "--watched"], CrashPolicy.RestartArguments(["--watchdog=99", "--watched"]));
    }

    [Fact]
    public void Parser_ReadsTheInternalWatchdogOptions()
    {
        var watchdog = CommandLineParser.Parse(["--background", "--watchdog", "4321"]);
        Assert.Empty(watchdog.Errors);
        Assert.Equal(4321, watchdog.WatchdogFor);
        Assert.True(watchdog.IsCommand);

        var watched = CommandLineParser.Parse(["--watched"]);
        Assert.Empty(watched.Errors);
        Assert.True(watched.Watched);
        Assert.False(watched.IsCommand);

        Assert.NotEmpty(CommandLineParser.Parse(["--watchdog", "abc"]).Errors);
    }

    [Fact]
    public void Config_WatchdogIsOnByDefault()
    {
        var log = new MemoryLog();
        Assert.True(new LooppaperConfig().Watchdog);
        Assert.False(ConfigLoader.Parse("""{ "watchdog": false }""", log).Watchdog);
        Assert.Empty(log.Warnings);
    }
}

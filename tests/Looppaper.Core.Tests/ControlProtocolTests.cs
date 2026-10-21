using Looppaper.Core.CommandLine;
using Looppaper.Core.Control;
using Looppaper.Core.Playback;

namespace Looppaper.Core.Tests;

public class ControlProtocolTests
{
    [Fact]
    public void Reply_RoundTripsThroughTheMessageResult()
    {
        var reply = new ControlReply(ControlState.Running | ControlState.Video | ControlState.PausedByUser, PauseReason.Locked | PauseReason.OnBattery);

        Assert.Equal(reply, ControlReply.Decode(reply.Encode()));
    }

    [Fact]
    public void Reply_NeverCarriesTheUserReasonInTheAutomaticOnes()
    {
        var reply = new ControlReply(ControlState.Running, PauseReason.User | PauseReason.DisplayOff);

        Assert.Equal(PauseReason.DisplayOff, ControlReply.Decode(reply.Encode()).AutoPause);
    }

    [Fact]
    public void FailedSend_DecodesAsNotRunning()
    {
        Assert.False(ControlReply.Decode(0).State.HasFlag(ControlState.Running));
    }

    [Fact]
    public void Describe_Playing()
    {
        Assert.Equal("Playing a video.", new ControlReply(ControlState.Running | ControlState.Video, PauseReason.None).Describe());
        Assert.Equal("Showing a slideshow.", new ControlReply(ControlState.Running | ControlState.Pictures | ControlState.Slideshow, PauseReason.None).Describe());
        Assert.Equal("Showing one picture.", new ControlReply(ControlState.Running | ControlState.Pictures, PauseReason.None).Describe());
    }

    [Fact]
    public void Describe_NamesEveryPauseReason()
    {
        var reply = new ControlReply(ControlState.Running | ControlState.Video | ControlState.PausedByUser, PauseReason.Locked | PauseReason.BatterySaver);

        Assert.True(reply.IsPaused);
        Assert.Equal("Playing a video; paused (you paused it; --resume continues; automatically: the PC is locked, battery saver is on).", reply.Describe());
    }

    [Fact]
    public void Describe_CoveredMonitorOnly()
    {
        var reply = new ControlReply(ControlState.Running | ControlState.Video | ControlState.MonitorCovered, PauseReason.None);

        Assert.False(reply.IsPaused);
        Assert.Contains("full-screen window covers", reply.Describe());
    }

    [Fact]
    public void Describe_WaitingForDesktop()
    {
        Assert.StartsWith("Waiting for the Windows desktop", new ControlReply(ControlState.Running | ControlState.WaitingForDesktop, PauseReason.None).Describe());
    }

    [Fact]
    public void DescribeResult_Resume_SaysWhenItStaysPaused()
    {
        var playing = new ControlReply(ControlState.Running | ControlState.Video, PauseReason.None);
        var locked = new ControlReply(ControlState.Running | ControlState.Video, PauseReason.Locked);

        Assert.Equal("Resumed. Playing a video.", playing.DescribeResult(ControlCommand.Resume));
        Assert.StartsWith("Resumed, but it is still paused automatically.", locked.DescribeResult(ControlCommand.Resume));
    }

    [Fact]
    public void DescribeResult_Next_SaysWhyNothingChanged()
    {
        var video = new ControlReply(ControlState.Running | ControlState.Video | ControlState.NotApplicable, PauseReason.None);
        var single = new ControlReply(ControlState.Running | ControlState.Pictures | ControlState.NotApplicable, PauseReason.None);
        var slideshow = new ControlReply(ControlState.Running | ControlState.Pictures | ControlState.Slideshow, PauseReason.None);

        var videos = new ControlReply(ControlState.Running | ControlState.Video | ControlState.Slideshow, PauseReason.None);

        Assert.StartsWith("There is no other video", video.DescribeResult(ControlCommand.Next));
        Assert.StartsWith("Single-picture mode", single.DescribeResult(ControlCommand.Previous));
        Assert.Equal("Switching to the next picture. Showing a slideshow.", slideshow.DescribeResult(ControlCommand.Next));
        Assert.Equal("Switching to the previous video. Playing videos in turn.", videos.DescribeResult(ControlCommand.Previous));
    }

    [Fact]
    public void DescribeResult_Pause()
    {
        var reply = new ControlReply(ControlState.Running | ControlState.Pictures | ControlState.Slideshow | ControlState.PausedByUser, PauseReason.None);

        Assert.Equal("Paused until you run --resume. Showing a slideshow; paused (you paused it; --resume continues).", reply.DescribeResult(ControlCommand.Pause));
    }

    [Theory]
    [InlineData("--pause", ControlCommand.Pause)]
    [InlineData("--resume", ControlCommand.Resume)]
    [InlineData("--status", ControlCommand.Status)]
    [InlineData("--next", ControlCommand.Next)]
    [InlineData("--previous", ControlCommand.Previous)]
    [InlineData("--prev", ControlCommand.Previous)]
    public void Parser_ControlCommands(string arg, ControlCommand expected)
    {
        var options = CommandLineParser.Parse([arg]);

        Assert.Empty(options.Errors);
        Assert.Equal(expected, options.Control);
        Assert.True(options.IsCommand);
    }

    [Fact]
    public void Parser_TwoControlCommands_IsAnError()
    {
        Assert.NotEmpty(CommandLineParser.Parse(["--pause", "--resume"]).Errors);
        Assert.Empty(CommandLineParser.Parse(["--pause", "--pause"]).Errors);
        Assert.NotEmpty(CommandLineParser.Parse(["--pause=yes"]).Errors);
    }
}

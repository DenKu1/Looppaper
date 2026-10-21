using Looppaper.Core.Compression;
using Looppaper.Core.Media;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Tests;

public class CompressionPlannerTests
{
    private static MediaInfo Info(double seconds, int width = 1920, int height = 1080, bool audio = true, long? bitRate = null, long? size = null, double fps = 30) =>
        new(TimeSpan.FromSeconds(seconds), width, height, "h264", fps, audio, bitRate, size);

    [Fact]
    public void BitrateComesFromSizeAndFrameRateNotFromALimit()
    {
        var plan = CompressionPlanner.Plan(Info(60), null, out var problem);

        Assert.Null(problem);
        Assert.NotNull(plan);
        Assert.Equal(CompressionPlanner.AudioKbps, plan.AudioKbps);

        // 0.10 bits per pixel * 1920 * 1080 * 30 fps = 6220 kbit/s.
        Assert.Equal(6_220, plan.VideoKbps);
    }

    [Fact]
    public void AnyLengthCanBePlanned()
    {
        var plan = CompressionPlanner.Plan(Info(4 * 3600), null, out var problem);

        Assert.Null(problem);
        Assert.NotNull(plan);
        Assert.True(VideoSize.IsLarge(plan.EstimatedBytes)); // long videos may stay large; they get a warning, not a refusal
    }

    [Fact]
    public void NeverSpendsMoreBitsThanTheOriginal()
    {
        // A 3 Mbit/s original keeps at most its own video bitrate (3000 - 128 audio).
        var plan = CompressionPlanner.Plan(Info(60, bitRate: 3_000_000), null, out _)!;

        Assert.Equal(3_000 - 128, plan.VideoKbps);
    }

    [Fact]
    public void HighFrameRateGetsMoreBitsButIsCapped()
    {
        var at60 = CompressionPlanner.Plan(Info(60, fps: 60), null, out _)!;
        var at240 = CompressionPlanner.Plan(Info(60, fps: 240), null, out _)!;

        Assert.Equal(12_441, at60.VideoKbps);
        Assert.Equal(at60.VideoKbps, at240.VideoKbps); // frame rate counted up to 60
    }

    [Fact]
    public void SilentVideoHasNoAudioTrack()
    {
        var plan = CompressionPlanner.Plan(Info(60, audio: false), null, out _)!;

        Assert.False(plan.KeepsAudio);
        Assert.DoesNotContain("audio", plan.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void CompactOriginalIsNotWorthCompressing()
    {
        // Already about the size the plan would produce: keep it.
        var compact = CompressionPlanner.Plan(Info(60, bitRate: 6_000_000, size: 45 * MediaScanner.BytesPerMb), null, out _)!;
        var bloated = CompressionPlanner.Plan(Info(60, bitRate: 30_000_000, size: 220 * MediaScanner.BytesPerMb), null, out _)!;

        Assert.False(compact.Worthwhile);
        Assert.True(bloated.Worthwhile);
    }

    [Fact]
    public void ScalingDownIsAlwaysWorthwhile()
    {
        var plan = CompressionPlanner.Plan(Info(60, 3840, 2160, bitRate: 2_000_000, size: 15 * MediaScanner.BytesPerMb), new PixelSize(2560, 1440), out _)!;

        Assert.True(plan.Scaled);
        Assert.True(plan.Worthwhile);
    }

    [Fact]
    public void MissingDurationOrVideoCannotBePlanned()
    {
        Assert.Null(CompressionPlanner.Plan(Info(60) with { Duration = null }, null, out var p1));
        Assert.Contains("length", p1, StringComparison.Ordinal);
        Assert.Null(CompressionPlanner.Plan(Info(60, 0, 0), null, out var p2));
        Assert.Contains("video stream", p2, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3840, 2160, 2560, 1440, 2560, 1440)] // 4K on 1440p: down to the screen
    [InlineData(1920, 1080, 2560, 1440, 1920, 1080)] // smaller than the screen: never up
    [InlineData(5120, 1440, 2560, 1440, 5120, 1440)] // ultrawide: keeps the height that covers the screen
    [InlineData(3840, 2160, 1920, 1200, 2132, 1200)] // 16:9 on 16:10: covers, aspect kept, even
    [InlineData(2160, 3840, 2560, 1440, 2160, 3840)] // portrait: narrower than the screen, kept
    [InlineData(1921, 1081, 0, 0, 1920, 1080)] // no screen: only made even
    public void ScalesDownToCoverTheScreen(int w, int h, int sw, int sh, int ew, int eh)
    {
        var output = CompressionPlanner.ScaleToCover(new PixelSize(w, h), sw > 0 ? new PixelSize(sw, sh) : null);

        Assert.Equal(new PixelSize(ew, eh), output);
    }

    [Fact]
    public void PlanCarriesTheScaledSize()
    {
        var plan = CompressionPlanner.Plan(Info(60, 3840, 2160), new PixelSize(2560, 1440), out _)!;

        Assert.True(plan.Scaled);
        Assert.Equal(new PixelSize(2560, 1440), plan.Output);
        Assert.Contains("2560x1440 (scaled down from 3840x2160)", plan.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeShowsSizeBitrateAndLength()
    {
        var plan = CompressionPlanner.Plan(Info(65), null, out _)!;

        Assert.Matches(@"^about \d+\.\d MB \(1920x1080, \d+\.\d Mbit/s video \+ 128 kbit/s audio, 1:05\)$", plan.Describe());
    }
}

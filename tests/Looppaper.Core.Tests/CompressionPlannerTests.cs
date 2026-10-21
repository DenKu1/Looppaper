using Looppaper.Core.Compression;
using Looppaper.Core.Media;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Tests;

public class CompressionPlannerTests
{
    private const long Limit = 100 * MediaScanner.BytesPerMb;

    private static MediaInfo Info(double seconds, int width = 1920, int height = 1080, bool audio = true) =>
        new(TimeSpan.FromSeconds(seconds), width, height, "h264", 30, audio, null, null);

    [Fact]
    public void BitrateFitsUnderTheLimitWithMargin()
    {
        var plan = CompressionPlanner.Plan(Info(60), Limit, null, out var problem);

        Assert.Null(problem);
        Assert.NotNull(plan);
        Assert.Equal(CompressionPlanner.AudioKbps, plan.AudioKbps);

        // 100 MiB * 0.9 * 8 / 60 s = 12582 kbit/s in total, minus audio.
        Assert.Equal(12_582 - 128, plan.VideoKbps);
        Assert.True(plan.EstimatedBytes < Limit);
        Assert.True(plan.EstimatedBytes > Limit * 0.85);
    }

    [Fact]
    public void LongerVideoGetsLowerBitrate()
    {
        var short1 = CompressionPlanner.Plan(Info(60), Limit, null, out _)!;
        var long1 = CompressionPlanner.Plan(Info(600), Limit, null, out _)!;

        Assert.True(long1.VideoKbps < short1.VideoKbps / 9);
    }

    [Fact]
    public void ShortVideoIsCappedAtAReasonableBitrate()
    {
        var plan = CompressionPlanner.Plan(Info(5), Limit, null, out _)!;

        Assert.Equal(CompressionPlanner.MaxVideoKbps, plan.VideoKbps);
    }

    [Fact]
    public void AudioIsDroppedOnlyWhenNeeded()
    {
        // ~1890 s: total budget ~400 + a bit; with audio the video would get less than the minimum.
        var plan = CompressionPlanner.Plan(Info(1700), Limit, null, out _);

        Assert.NotNull(plan);
        Assert.False(plan.KeepsAudio);
        Assert.True(plan.AudioDropped);
        Assert.Contains("audio removed", plan.Describe(), StringComparison.Ordinal);

        var silent = CompressionPlanner.Plan(Info(60, audio: false), Limit, null, out _)!;
        Assert.False(silent.AudioDropped);
        Assert.Equal(12_582, silent.VideoKbps);
    }

    [Fact]
    public void TooLongVideoCannotBePlanned()
    {
        var plan = CompressionPlanner.Plan(Info(4 * 3600), Limit, null, out var problem);

        Assert.Null(plan);
        Assert.Contains("too long", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDurationOrVideoCannotBePlanned()
    {
        Assert.Null(CompressionPlanner.Plan(Info(60) with { Duration = null }, Limit, null, out var p1));
        Assert.Contains("length", p1, StringComparison.Ordinal);
        Assert.Null(CompressionPlanner.Plan(Info(60, 0, 0), Limit, null, out var p2));
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
        var plan = CompressionPlanner.Plan(Info(60, 3840, 2160), Limit, new PixelSize(2560, 1440), out _)!;

        Assert.True(plan.Scaled);
        Assert.Equal(new PixelSize(2560, 1440), plan.Output);
        Assert.Contains("2560x1440 (scaled down from 3840x2160)", plan.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void RetryMarginIsLowerByAtLeastFifteenPercent()
    {
        Assert.Equal(0.9 * 0.85, CompressionPlanner.RetryMargin(0.9, Limit + 1, Limit), 3);

        var bigOvershoot = CompressionPlanner.RetryMargin(0.9, Limit * 2, Limit);
        Assert.True(bigOvershoot < 0.45);
    }

    [Fact]
    public void DescribeShowsSizeBitrateAndLength()
    {
        var plan = CompressionPlanner.Plan(Info(65), Limit, null, out _)!;

        Assert.Matches(@"^about \d+\.\d MB \(1920x1080, \d+\.\d Mbit/s video \+ 128 kbit/s audio, 1:05\)$", plan.Describe());
    }
}

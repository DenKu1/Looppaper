using Looppaper.Core.Configuration;
using Looppaper.Core.Rendering;

namespace Looppaper.Core.Tests;

public class ScalingMathTests
{
    private static readonly IntRect Monitor = new(100, 50, 1920, 1080);

    [Fact]
    public void StretchCoversMonitorExactly()
    {
        Assert.Equal(Monitor, ScalingMath.Destination(640, 480, Monitor, ScalingMode.Stretch));
    }

    [Fact]
    public void FillCoversAndCropsWiderAxis()
    {
        // 4:3 on 16:9: width matches, height overflows and is centered.
        var dest = ScalingMath.Destination(800, 600, Monitor, ScalingMode.Fill);

        Assert.Equal(new IntRect(100, 50 - 180, 1920, 1440), dest);
    }

    [Fact]
    public void FitLetterboxes()
    {
        // 4:3 on 16:9: height matches, pillarboxed horizontally.
        var dest = ScalingMath.Destination(800, 600, Monitor, ScalingMode.Fit);

        Assert.Equal(new IntRect(100 + 240, 50, 1440, 1080), dest);
    }

    [Fact]
    public void FitUpscalesSmallPictures()
    {
        var dest = ScalingMath.Destination(192, 108, Monitor, ScalingMode.Fit);

        Assert.Equal(Monitor, dest);
    }

    [Fact]
    public void CenterKeepsOriginalSize()
    {
        Assert.Equal(new IntRect(100 + 860, 50 + 490, 200, 100), ScalingMath.Destination(200, 100, Monitor, ScalingMode.Center));
        Assert.Equal(new IntRect(100 - 40, 50 - 10, 2000, 1100), ScalingMath.Destination(2000, 1100, Monitor, ScalingMode.Center));
    }

    [Fact]
    public void SameAspectRatioMatchesMonitorInFillAndFit()
    {
        Assert.Equal(Monitor, ScalingMath.Destination(3840, 2160, Monitor, ScalingMode.Fill));
        Assert.Equal(Monitor, ScalingMath.Destination(3840, 2160, Monitor, ScalingMode.Fit));
    }

    [Fact]
    public void RoundingWithinOnePixelSnapsToEdge()
    {
        // 1921x1080 would round to 1919 wide in fit mode; it should still cover the monitor width.
        var dest = ScalingMath.Destination(1921, 1081, Monitor, ScalingMode.Fit);

        Assert.Equal(Monitor.Width, dest.Width);
        Assert.Equal(Monitor.X, dest.X);
    }

    [Fact]
    public void DegenerateInputsGiveEmptyRect()
    {
        Assert.Equal(0, ScalingMath.Destination(0, 100, Monitor, ScalingMode.Fill).Width);
        Assert.Equal(0, ScalingMath.Destination(100, 100, new IntRect(0, 0, 0, 0), ScalingMode.Fit).Width);
    }
}

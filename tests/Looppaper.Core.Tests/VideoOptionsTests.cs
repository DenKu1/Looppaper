using Looppaper.Core.CommandLine;
using Looppaper.Core.Configuration;
using Looppaper.Core.Rendering;

namespace Looppaper.Core.Tests;

public class VideoOptionsTests
{
    [Fact]
    public void VideoOptionEnablesVideoAndSetsFile()
    {
        var config = new LooppaperConfig();
        config.Video.Enabled = false;

        CommandLineParser.Parse(["--video", "waves.mp4", "--volume=25"]).ApplyTo(config);

        Assert.True(config.Video.Enabled);
        Assert.Equal("waves.mp4", config.Video.File);
        Assert.Equal(25, config.Video.Volume);
    }

    [Fact]
    public void PictureOptionDisablesVideo()
    {
        var config = new LooppaperConfig();

        CommandLineParser.Parse(["--picture", "cat.png"]).ApplyTo(config);

        Assert.False(config.Video.Enabled);
        Assert.Equal(WallpaperMode.Single, config.Mode);
    }

    [Theory]
    [InlineData("--video", "a.mp4", "--no-video")]
    [InlineData("--volume", "loud")]
    [InlineData("--volume")]
    [InlineData("--no-video=yes")]
    public void InvalidVideoOptions(params string[] args)
    {
        Assert.NotEmpty(CommandLineParser.Parse(args).Errors);
    }

    [Fact]
    public void VolumeIsClamped()
    {
        var config = new LooppaperConfig();
        CommandLineParser.Parse(["--volume", "150"]).ApplyTo(config);

        ConfigValidator.Normalize(config, new MemoryLog());

        Assert.Equal(100, config.Video.Volume);
    }

    [Fact]
    public void VideoSizeLimitIsHundredMbAndCannotBeRaised()
    {
        var log = new MemoryLog();
        var config = ConfigLoader.Parse("""{ "video": { "maxFileSizeMb": 500 } }""", log);

        ConfigValidator.Normalize(config, log);

        Assert.Equal(100, new LooppaperConfig().Video.MaxFileSizeMb);
        Assert.Equal(100, config.Video.MaxFileSizeMb);
        Assert.Contains(log.Warnings, w => w.Contains("video.maxFileSizeMb", StringComparison.Ordinal) && w.Contains("using 100", StringComparison.Ordinal));
    }

    [Fact]
    public void LowerVideoSizeLimitIsKept()
    {
        var log = new MemoryLog();
        var config = ConfigLoader.Parse("""{ "video": { "maxFileSizeMb": 40 } }""", log);

        ConfigValidator.Normalize(config, log);

        Assert.Equal(40, config.Video.MaxFileSizeMb);
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void VideoOverHundredMbIsNotListed()
    {
        using var dir = new TempDir();
        dir.File("small.mp4", 99 * Media.MediaScanner.BytesPerMb);
        dir.File("huge.mp4", (100 * Media.MediaScanner.BytesPerMb) + 1);
        var log = new MemoryLog();

        var videos = new Media.MediaScanner(log).Scan(dir.Path, Media.MediaKind.Video, new LooppaperConfig().Video.MaxFileSizeMb);

        Assert.Equal(["small.mp4"], videos.Select(Path.GetFileName));
        Assert.Contains(log.Warnings, w => w.Contains("huge.mp4", StringComparison.Ordinal) && w.Contains("100 MB limit", StringComparison.Ordinal));
    }

    [Fact]
    public void VideoIsMutedAndEnabledByDefault()
    {
        var video = new LooppaperConfig().Video;

        Assert.True(video.Enabled);
        Assert.Equal(0, video.Volume);
        Assert.Null(video.File);
    }

    [Fact]
    public void Viewport_InsideClipUsesWholeFrame()
    {
        var clip = new IntRect(0, 0, 1920, 1080);
        var (source, destination) = VideoViewport.Visible(new IntRect(240, 0, 1440, 1080), clip);

        Assert.Equal(NormalizedRect.Full, source);
        Assert.Equal(new IntRect(240, 0, 1440, 1080), destination);
    }

    [Fact]
    public void Viewport_FillCropsTopAndBottom()
    {
        // 4:3 filling 16:9: destination 1920x1440 centred, 180 px cut off top and bottom.
        var clip = new IntRect(0, 0, 1920, 1080);
        var (source, destination) = VideoViewport.Visible(new IntRect(0, -180, 1920, 1440), clip);

        Assert.Equal(clip, destination);
        Assert.Equal(0f, source.Left);
        Assert.Equal(1f, source.Right);
        Assert.Equal(0.125f, source.Top, 4);
        Assert.Equal(0.875f, source.Bottom, 4);
    }

    [Fact]
    public void Viewport_NoOverlapIsEmpty()
    {
        var (_, destination) = VideoViewport.Visible(new IntRect(5000, 0, 100, 100), new IntRect(0, 0, 1920, 1080));

        Assert.Equal(0, destination.Width);
    }
}

using Looppaper.Core.Configuration;

namespace Looppaper.Core.Tests;

public class ConfigModelTests
{
    [Theory]
    [InlineData("#1A2B3C", 0x1A, 0x2B, 0x3C)]
    [InlineData("ffffff", 0xFF, 0xFF, 0xFF)]
    [InlineData(" #000000 ", 0, 0, 0)]
    public void RgbColor_ParsesHex(string text, byte r, byte g, byte b)
    {
        Assert.True(RgbColor.TryParse(text, out var color));
        Assert.Equal(new RgbColor(r, g, b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void RgbColor_RejectsInvalid(string? text)
    {
        Assert.False(RgbColor.TryParse(text, out _));
    }

    [Fact]
    public void RgbColor_ColorRefIsBgr()
    {
        Assert.Equal(0x00332211u, new RgbColor(0x11, 0x22, 0x33).ToColorRef());
        Assert.Equal("#112233", new RgbColor(0x11, 0x22, 0x33).ToString());
    }

    [Fact]
    public void ResolvePicture_RelativeIsInsideCollection()
    {
        var root = Path.GetTempPath();
        var config = new LooppaperConfig { CollectionFolder = root, Picture = "a.png" };

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "a.png"), config.ResolvePicture());
    }

    [Fact]
    public void ResolvePicture_AbsoluteIsKept()
    {
        var abs = Path.Combine(Path.GetTempPath(), "x", "b.jpg");
        var config = new LooppaperConfig { CollectionFolder = "C:\\elsewhere", Picture = abs };

        Assert.Equal(abs, config.ResolvePicture());
    }

    [Fact]
    public void ResolveVideo_NullUntilConfiguredThenInsideCollection()
    {
        var config = new LooppaperConfig { CollectionFolder = Path.GetTempPath() };
        Assert.Null(config.ResolveVideo());

        config.Video.File = "waves.mp4";

        Assert.Equal(Path.Combine(config.ResolveCollectionFolder(), "waves.mp4"), config.ResolveVideo());
    }

    [Fact]
    public void ResolveVideo_FileNameIsLookedUpInTheVideoFolderFirst()
    {
        using var pictures = new TempDir();
        using var videos = new TempDir();
        var config = new LooppaperConfig { CollectionFolder = pictures.Path };
        config.Video.Folder = videos.Path;
        config.Video.File = "waves.mp4";

        Assert.Equal(Path.Combine(pictures.Path, "waves.mp4"), config.ResolveVideo()); // in neither: the collection, as before

        var inVideos = videos.File("waves.mp4");

        Assert.Equal(inVideos, config.ResolveVideo());
        Assert.Equal([videos.Path, pictures.Path], config.ResolveVideoFolders());
    }

    [Fact]
    public void ResolveVideoFolders_SameFolderOnce()
    {
        using var dir = new TempDir();
        var config = new LooppaperConfig { CollectionFolder = dir.Path + Path.DirectorySeparatorChar };
        config.Video.Folder = dir.Path;

        Assert.Single(config.ResolveVideoFolders());
    }

    [Fact]
    public void VideoFolder_IsParsedAndAnEmptyOneFallsBackToTheDefault()
    {
        var log = new MemoryLog();
        Assert.Equal(@"D:\Clips", ConfigLoader.Parse("""{ "video": { "folder": "D:\\Clips" } }""", log).Video.Folder);
        Assert.Empty(log.Warnings);
        Assert.Contains("\"folder\"", ConfigLoader.Serialize(new LooppaperConfig()), StringComparison.Ordinal);

        var empty = ConfigLoader.Parse("""{ "video": { "folder": " " } }""", log);
        ConfigValidator.Normalize(empty, log);
        Assert.Equal(LooppaperConfig.DefaultVideoFolder, empty.Video.Folder);
    }

    [Fact]
    public void ResolveCollectionFolder_ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("LOOPPAPER_TEST_ROOT", Path.GetTempPath());
        var config = new LooppaperConfig { CollectionFolder = Path.Combine("%LOOPPAPER_TEST_ROOT%", "pics") };

        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pics")), config.ResolveCollectionFolder());
    }

    [Fact]
    public void Validator_EmptyFolderFallsBackToDefault()
    {
        var log = new MemoryLog();
        var config = new LooppaperConfig { CollectionFolder = "  " };

        ConfigValidator.Normalize(config, log);

        Assert.Equal(LooppaperConfig.DefaultCollectionFolder, config.CollectionFolder);
        Assert.Single(log.Warnings);
    }
}

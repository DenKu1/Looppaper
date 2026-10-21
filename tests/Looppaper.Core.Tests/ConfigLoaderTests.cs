using Looppaper.Core.Configuration;

namespace Looppaper.Core.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void LoadOrCreate_WritesDefaultsWhenMissing()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sub", "config.json");
        var log = new MemoryLog();

        var config = ConfigLoader.LoadOrCreate(path, log);

        Assert.True(File.Exists(path));
        Assert.Equal(LooppaperConfig.DefaultCollectionFolder, config.CollectionFolder);
        var text = File.ReadAllText(path);
        Assert.Contains("\"collectionFolder\"", text);
        Assert.Contains("\"mode\": \"slideshow\"", text);
        Assert.Contains("\"scaling\": \"fill\"", text);
        Assert.Contains("\"compress\": true", text);
        Assert.DoesNotContain("maxFileSizeMb", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WrittenDefaults_RoundTripWithoutWarnings()
    {
        var log = new MemoryLog();
        var config = ConfigLoader.Parse(ConfigLoader.Serialize(new LooppaperConfig()), log);
        ConfigValidator.Normalize(config, log);

        Assert.Empty(log.Warnings);
        Assert.Empty(log.Errors);
        Assert.Equal(300, config.IntervalSeconds);
        Assert.Equal(0, config.Video.Volume);
        Assert.True(config.Video.Enabled);
        Assert.Null(config.Video.File);
    }

    [Fact]
    public void Parse_AcceptsCommentsTrailingCommasAndAnyCase()
    {
        const string json = """
            // my settings
            {
              "CollectionFolder": "D:\\Walls", /* inline */
              "mode": "Single",
              "picture": "cat.png",
              "scaling": "FIT",
              "shuffle": true,
              "video": { "enabled": false, "file": "waves.mp4", "volume": 70, },
            }
            """;
        var log = new MemoryLog();

        var config = ConfigLoader.Parse(json, log);

        Assert.Empty(log.Warnings);
        Assert.Equal(@"D:\Walls", config.CollectionFolder);
        Assert.Equal(WallpaperMode.Single, config.Mode);
        Assert.Equal("cat.png", config.Picture);
        Assert.Equal(ScalingMode.Fit, config.Scaling);
        Assert.True(config.Shuffle);
        Assert.False(config.Video.Enabled);
        Assert.Equal("waves.mp4", config.Video.File);
        Assert.Equal(70, config.Video.Volume);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[1, 2]")]
    [InlineData("42")]
    public void Parse_BrokenJsonFallsBackToDefaults(string json)
    {
        var log = new MemoryLog();

        var config = ConfigLoader.Parse(json, log);

        Assert.NotEmpty(log.Errors);
        Assert.Equal(LooppaperConfig.DefaultIntervalSeconds, config.IntervalSeconds);
        Assert.Equal(WallpaperMode.Slideshow, config.Mode);
    }

    [Fact]
    public void Parse_WrongTypesKeepDefaultsPerField()
    {
        const string json = """
            {
              "intervalSeconds": "fast",
              "mode": "carousel",
              "scaling": 3,
              "shuffle": "yes",
              "collectionFolder": null,
              "video": "loud",
              "intervalSecondz": 1
            }
            """;
        var log = new MemoryLog();

        var config = ConfigLoader.Parse(json, log);

        Assert.Equal(LooppaperConfig.DefaultIntervalSeconds, config.IntervalSeconds);
        Assert.Equal(WallpaperMode.Slideshow, config.Mode);
        Assert.Equal(ScalingMode.Fill, config.Scaling);
        Assert.False(config.Shuffle);
        Assert.Equal(LooppaperConfig.DefaultCollectionFolder, config.CollectionFolder);
        Assert.True(config.Video.Enabled);
        Assert.Equal(7, log.Warnings.Count());
        Assert.Contains(log.Warnings, w => w.Contains("intervalSecondz", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_NumericEnumStringIsRejected()
    {
        var log = new MemoryLog();

        var config = ConfigLoader.Parse("""{ "mode": "1" }""", log);

        Assert.Equal(WallpaperMode.Slideshow, config.Mode);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void LoadOrCreate_ClampsOutOfRangeValues()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, """
            {
              "intervalSeconds": 1,
              "maxPictureSizeMb": 5000,
              "backgroundColor": "red",
              "video": { "volume": 250, "maxFileSizeMb": 1e12 }
            }
            """);
        var log = new MemoryLog();

        var config = ConfigLoader.LoadOrCreate(path, log);

        Assert.Equal(LooppaperConfig.MinIntervalSeconds, config.IntervalSeconds);
        Assert.Equal(LooppaperConfig.MaxPictureSizeCapMb, config.MaxPictureSizeMb);
        Assert.Equal(LooppaperConfig.DefaultBackgroundColor, config.BackgroundColor);
        Assert.Equal(100, config.Video.Volume);
        Assert.Equal(4, log.Warnings.Count()); // the obsolete video.maxFileSizeMb is only noted, not warned about
    }

    [Fact]
    public void CheckForUpdates_IsOffByDefaultAndWritten()
    {
        var config = new LooppaperConfig();

        Assert.False(config.CheckForUpdates);
        Assert.Contains("\"checkForUpdates\": false", ConfigLoader.Serialize(config), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReadsCheckForUpdates()
    {
        var log = new MemoryLog();

        Assert.True(ConfigLoader.Parse("""{ "checkForUpdates": true }""", log).CheckForUpdates);
        Assert.False(ConfigLoader.Parse("""{ "checkForUpdates": "yes" }""", log).CheckForUpdates);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void TrayIcon_IsOnByDefaultAndWritten()
    {
        var config = new LooppaperConfig();

        Assert.True(config.TrayIcon);
        Assert.Contains("\"trayIcon\": true", ConfigLoader.Serialize(config), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReadsTrayIcon()
    {
        var log = new MemoryLog();

        Assert.False(ConfigLoader.Parse("""{ "trayIcon": false }""", log).TrayIcon);
        Assert.True(ConfigLoader.Parse("""{ "TrayIcon": 0 }""", log).TrayIcon);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void LoadOrCreate_DoesNotOverwriteBrokenFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, "{ oops");

        var config = ConfigLoader.LoadOrCreate(path, new MemoryLog());

        Assert.Equal("{ oops", File.ReadAllText(path));
        Assert.Equal(LooppaperConfig.DefaultIntervalSeconds, config.IntervalSeconds);
    }
}

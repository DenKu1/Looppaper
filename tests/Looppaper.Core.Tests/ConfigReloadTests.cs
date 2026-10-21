using Looppaper.Core.Configuration;

namespace Looppaper.Core.Tests;

public class ConfigReloadTests
{
    [Fact]
    public void TryLoad_ReadsAndNormalizes()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, "{ \"mode\": \"single\", \"intervalSeconds\": 1, // comment\n}");
        var log = new MemoryLog();

        Assert.True(ConfigLoader.TryLoad(path, log, out var config));

        Assert.Equal(WallpaperMode.Single, config.Mode);
        Assert.Equal(LooppaperConfig.MinIntervalSeconds, config.IntervalSeconds);
    }

    [Theory]
    [InlineData("""{ "mode": "single", """)] // half written
    [InlineData("[1, 2]")]
    [InlineData("")]
    public void TryLoad_KeepsCurrentSettingsForBrokenFiles(string text)
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, text);
        var log = new MemoryLog();

        Assert.False(ConfigLoader.TryLoad(path, log, out _));
        Assert.Contains(log.Warnings, w => w.Contains("keeping the current settings", StringComparison.Ordinal));
    }

    [Fact]
    public void WrittenDefaults_IncludeTheEpic2Settings()
    {
        var text = ConfigLoader.Serialize(new LooppaperConfig());
        var log = new MemoryLog();

        var config = ConfigLoader.Parse(text, log);

        Assert.Contains("\"watchdog\": true", text, StringComparison.Ordinal);
        Assert.Contains("\"onFullscreen\": true", text, StringComparison.Ordinal);
        Assert.Contains("\"onBattery\": false", text, StringComparison.Ordinal);
        Assert.Empty(log.Warnings);
        Assert.True(config.Pause.OnLock);
    }

    [Fact]
    public void TryLoad_MissingFile_IsNotCreated()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "config.json");

        Assert.False(ConfigLoader.TryLoad(path, new MemoryLog(), out _));
        Assert.False(File.Exists(path));
    }
}

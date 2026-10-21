using Looppaper.Core.CommandLine;
using Looppaper.Core.Configuration;

namespace Looppaper.Core.Tests;

public class ConfigEditorTests
{
    [Fact]
    public void SetVideoKeepsEveryOtherValue()
    {
        const string json = """
            // my settings
            {
              "CollectionFolder": "D:\\Walls",
              "intervalSeconds": 60,
              "Video": { "Enabled": false, "volume": 30, },
              "futureSetting": { "x": [1, 2] },
            }
            """;

        var text = ConfigEditor.SetVideoInText(json, @"C:\Steam\workshop\content\431960\42\clip.mp4");
        var log = new MemoryLog();
        var config = ConfigLoader.Parse(text, log);

        Assert.Equal(@"D:\Walls", config.CollectionFolder);
        Assert.Equal(60, config.IntervalSeconds);
        Assert.True(config.Video.Enabled);
        Assert.Equal(30, config.Video.Volume);
        Assert.Equal(@"C:\Steam\workshop\content\431960\42\clip.mp4", config.Video.File);
        Assert.Contains("\"futureSetting\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"enabled\"", text, StringComparison.Ordinal);
        Assert.StartsWith("// Looppaper configuration", text, StringComparison.Ordinal);
        Assert.Single(log.Warnings); // only the unknown futureSetting
    }

    [Fact]
    public void SetVideoAddsTheVideoSectionWhenMissingOrWrong()
    {
        var added = ConfigLoader.Parse(ConfigEditor.SetVideoInText("""{ "shuffle": true }""", "a.mp4"), new MemoryLog());
        var replaced = ConfigLoader.Parse(ConfigEditor.SetVideoInText("""{ "video": "loud" }""", "b.mp4"), new MemoryLog());

        Assert.True(added.Shuffle);
        Assert.Equal("a.mp4", added.Video.File);
        Assert.Equal("b.mp4", replaced.Video.File);
        Assert.True(replaced.Video.Enabled);
    }

    [Fact]
    public void SetVideoWritesTheFileAndCreatesItWhenMissing()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sub", "config.json");

        Assert.Null(ConfigEditor.SetVideo(path, @"E:\v\ünïcode.mp4"));

        var config = ConfigLoader.LoadOrCreate(path, new MemoryLog());
        Assert.Equal(@"E:\v\ünïcode.mp4", config.Video.File);
        Assert.Equal(LooppaperConfig.DefaultIntervalSeconds, config.IntervalSeconds);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void BrokenConfigIsLeftAlone()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, "{ broken");

        var error = ConfigEditor.SetVideo(path, "x.mp4");

        Assert.NotNull(error);
        Assert.Equal("{ broken", File.ReadAllText(path));
    }

    [Fact]
    public void ParsesWallpaperEngineCommands()
    {
        var set = CommandLineParser.Parse(["--we-set", "3", "--no-restart", "--config", "c.json"]);
        var pick = CommandLineParser.Parse(["--we-pick"]);
        var both = CommandLineParser.Parse(["--we-pick", "--we-list"]);
        var missing = CommandLineParser.Parse(["--we-set"]);

        Assert.Empty(set.Errors);
        Assert.Equal("3", set.WeSet);
        Assert.True(set.NoRestart);
        Assert.True(set.IsWallpaperEngineCommand);
        Assert.True(pick.WePick);
        Assert.True(pick.IsCommand);
        Assert.NotEmpty(both.Errors);
        Assert.NotEmpty(missing.Errors);
    }
}

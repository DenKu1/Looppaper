using Looppaper.Core.CommandLine;
using Looppaper.Core.Configuration;

namespace Looppaper.Core.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Empty_HasNoOverrides()
    {
        var options = CommandLineParser.Parse([]);

        Assert.Empty(options.Errors);
        Assert.False(options.IsCommand);
        Assert.Equal("(none)", options.ToString());
    }

    [Fact]
    public void ParsesValuesInBothForms()
    {
        var options = CommandLineParser.Parse(["--folder", @"D:\Pics", "--interval=60", "--mode", "SINGLE", "--config=c.json", "--shuffle", "--no-video", "--volume", "35"]);

        Assert.Empty(options.Errors);
        Assert.Equal(@"D:\Pics", options.Folder);
        Assert.Equal(60, options.IntervalSeconds);
        Assert.Equal(WallpaperMode.Single, options.Mode);
        Assert.Equal("c.json", options.ConfigPath);
        Assert.True(options.Shuffle);
        Assert.True(options.NoVideo);
        Assert.Equal(35, options.Volume);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void HelpAliases(string arg)
    {
        Assert.True(CommandLineParser.Parse([arg]).Help);
    }

    [Theory]
    [InlineData("--stop")]
    [InlineData("--install-startup")]
    [InlineData("--uninstall-startup")]
    public void Commands(string arg)
    {
        var options = CommandLineParser.Parse([arg]);

        Assert.Empty(options.Errors);
        Assert.True(options.IsCommand);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("stray")]
    [InlineData("--interval")]
    [InlineData("--interval", "soon")]
    [InlineData("--mode", "carousel")]
    [InlineData("--mode", "0")]
    [InlineData("--folder", "--shuffle")]
    [InlineData("--folder=")]
    [InlineData("--stop=now")]
    [InlineData("--install-startup", "--uninstall-startup")]
    public void InvalidInputReportsErrors(params string[] args)
    {
        Assert.NotEmpty(CommandLineParser.Parse(args).Errors);
    }

    [Fact]
    public void PictureImpliesSingleMode()
    {
        var config = new LooppaperConfig { Mode = WallpaperMode.Slideshow };

        CommandLineParser.Parse(["--picture", "sunset.jpg", "--mode", "slideshow"]).ApplyTo(config);

        Assert.Equal(WallpaperMode.Single, config.Mode);
        Assert.Equal("sunset.jpg", config.Picture);
    }

    [Fact]
    public void ApplyTo_OnlyTouchesGivenOptions()
    {
        var config = new LooppaperConfig { IntervalSeconds = 42, Shuffle = true, CollectionFolder = "X" };

        CommandLineParser.Parse(["--no-video"]).ApplyTo(config);

        Assert.Equal(42, config.IntervalSeconds);
        Assert.True(config.Shuffle);
        Assert.Equal("X", config.CollectionFolder);
        Assert.False(config.Video.Enabled);
    }

    [Fact]
    public void OverriddenIntervalIsClampedByValidator()
    {
        var config = new LooppaperConfig();
        CommandLineParser.Parse(["--interval", "2"]).ApplyTo(config);

        ConfigValidator.Normalize(config, new MemoryLog());

        Assert.Equal(LooppaperConfig.MinIntervalSeconds, config.IntervalSeconds);
    }
}

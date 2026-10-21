using Looppaper.Core.Configuration;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Tests;

public class FfmpegToolsTests
{
    private sealed class FakeEnvironment : IToolEnvironment
    {
        public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Executables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? GetEnvironmentVariable(string name) => Variables.GetValueOrDefault(name);

        public bool FileExists(string path) => Executables.ContainsKey(path) || Files.Contains(path);

        public bool DirectoryExists(string path) => Directories.ContainsKey(path);

        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
        {
            var prefix = searchPattern.Trim('*');
            return Directories.GetValueOrDefault(path, [])
                .Where(d => prefix.Length == 0 || Path.GetFileName(d).Contains(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public string? ReadVersionLine(string exe) => Executables.GetValueOrDefault(exe);
    }

    [Theory]
    [InlineData("ffmpeg version 8.0.1-full_build-www.gyan.dev Copyright (c) 2000-2025", 8, 0, 1)]
    [InlineData("ffmpeg version n7.1 Copyright (c) 2000-2024", 7, 1, 0)]
    [InlineData("ffmpeg version 6.1.1-essentials_build-www.gyan.dev", 6, 1, 1)]
    public void ParsesReleaseVersions(string line, int major, int minor, int build)
    {
        var version = FfmpegVersion.Parse(line);

        Assert.Equal(new Version(major, minor, build), version.Number);
    }

    [Theory]
    [InlineData("ffmpeg version 8.0-full_build", 8, true)]
    [InlineData("ffmpeg version 7.1.1-full_build", 8, false)]
    [InlineData("ffmpeg version 2025-10-01-git-a1b2c3d4e5-full_build-www.gyan.dev", 8, true)]
    [InlineData("ffmpeg version 2025-03-01-git-a1b2c3d4e5-full_build-www.gyan.dev", 8, false)]
    [InlineData("ffmpeg version 2025-03-01-git-a1b2c3d4e5-full_build-www.gyan.dev", 7, true)]
    [InlineData("ffmpeg version N-121000-gabcdef-20251001 Copyright", 8, true)]
    [InlineData("something unexpected", 8, false)]
    public void VersionRequirement(string line, int major, bool expected)
    {
        Assert.Equal(expected, FfmpegVersion.Parse(line).IsAtLeast(major));
    }

    [Fact]
    public void ConfiguredPathWinsAndFindsFfprobe()
    {
        var env = new FakeEnvironment();
        env.Variables["PATH"] = @"C:\tools";
        env.Executables[@"C:\tools\ffmpeg.exe"] = "ffmpeg version 8.0";
        env.Executables[@"D:\ff\bin\ffmpeg.exe"] = "ffmpeg version 8.0.1";
        env.Files.Add(@"D:\ff\bin\ffprobe.exe");

        var tool = new FfmpegLocator(env).Find(@"D:\ff", new MemoryLog());

        Assert.NotNull(tool);
        Assert.Equal(@"D:\ff\bin\ffmpeg.exe", tool.FfmpegPath);
        Assert.Equal(@"D:\ff\bin\ffprobe.exe", tool.FfprobePath);
    }

    [Fact]
    public void FindsWingetGyanPackage()
    {
        var env = new FakeEnvironment();
        env.Variables["LOCALAPPDATA"] = @"C:\Users\me\AppData\Local";
        var packages = @"C:\Users\me\AppData\Local\Microsoft\WinGet\Packages";
        var package = packages + @"\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe";
        env.Directories[packages] = [package, packages + @"\Other.Tool_x"];
        env.Directories[package] = [package + @"\ffmpeg-8.0.1-full_build"];
        env.Executables[package + @"\ffmpeg-8.0.1-full_build\bin\ffmpeg.exe"] = "ffmpeg version 8.0.1-full_build-www.gyan.dev";

        var tool = new FfmpegLocator(env).Find(null, new MemoryLog());

        Assert.Equal(package + @"\ffmpeg-8.0.1-full_build\bin\ffmpeg.exe", tool?.FfmpegPath);
        Assert.Null(tool?.FfprobePath);
    }

    [Fact]
    public void SkipsTooOldAndBrokenCandidates()
    {
        var env = new FakeEnvironment();
        env.Variables["PATH"] = @"C:\old;C:\broken;C:\new";
        env.Executables[@"C:\old\ffmpeg.exe"] = "ffmpeg version 6.0";
        env.Files.Add(@"C:\broken\ffmpeg.exe"); // exists but cannot run
        env.Executables[@"C:\new\ffmpeg.exe"] = "ffmpeg version 8.0";
        var log = new MemoryLog();

        var tool = new FfmpegLocator(env).Find(null, log, minimumMajor: 8);

        Assert.Equal(@"C:\new\ffmpeg.exe", tool?.FfmpegPath);
        Assert.Equal(2, log.Warnings.Count());
    }

    [Fact]
    public void ReturnsNullWhenNothingFound()
    {
        Assert.Null(new FfmpegLocator(new FakeEnvironment()).Find(null, new MemoryLog()));
    }

    [Fact]
    public void ProgressParserReportsFractionAndEnd()
    {
        var parser = new FfmpegProgressParser(TimeSpan.FromSeconds(20));

        Assert.Null(parser.Feed("frame=120"));
        Assert.Null(parser.Feed("out_time_us=5000000"));
        Assert.Null(parser.Feed("speed=2.5x"));
        var mid = parser.Feed("progress=continue");
        parser.Feed("out_time_us=20000000");
        var end = parser.Feed("progress=end");

        Assert.Equal(TimeSpan.FromSeconds(5), mid?.Position);
        Assert.Equal(0.25, mid?.Fraction);
        Assert.Equal(2.5, mid?.Speed);
        Assert.False(mid?.Finished);
        Assert.Equal(1.0, end?.Fraction);
        Assert.True(end?.Finished);
    }

    [Fact]
    public void ProgressParserWithoutDurationHasNoFraction()
    {
        var parser = new FfmpegProgressParser(null);
        parser.Feed("out_time_us=1000000");
        parser.Feed("speed=N/A");

        var snapshot = parser.Feed("progress=continue");

        Assert.Null(snapshot?.Fraction);
        Assert.Null(snapshot?.Speed);
    }

    [Fact]
    public void ParsesFfprobeJson()
    {
        const string json = """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "mjpeg", "width": 300, "height": 300, "disposition": { "attached_pic": 1 } },
                { "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, "avg_frame_rate": "30000/1001" },
                { "codec_type": "audio", "codec_name": "aac" }
              ],
              "format": { "duration": "62.500000", "bit_rate": "27900000", "size": "228491264" }
            }
            """;

        var info = MediaProbe.Parse(json);

        Assert.NotNull(info);
        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal(29.97, info.FrameRate!.Value, 2);
        Assert.True(info.HasAudio);
        Assert.Equal(TimeSpan.FromSeconds(62.5), info.Duration);
        Assert.Equal(27_900_000, info.BitRate);
        Assert.Equal(228_491_264, info.SizeBytes);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public void FfprobeParseIsTolerant(string json)
    {
        var info = MediaProbe.Parse(json);

        Assert.True(info is null || info.Width == 0);
    }

    [Fact]
    public void ConfigReadsFfmpegPath()
    {
        var config = ConfigLoader.Parse("""{ "ffmpegPath": "D:\\tools\\ffmpeg" }""", new MemoryLog());

        Assert.Equal(@"D:\tools\ffmpeg", config.FfmpegPath);
        Assert.Null(new LooppaperConfig().FfmpegPath);
    }

    [Fact]
    public async Task RunnerReportsProgressFromAFakeFfmpeg()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var script = Path.Combine(dir.Path, "ffmpeg.cmd");
        File.WriteAllText(script, string.Join("\r\n",
            "@echo off",
            "echo out_time_us=2000000",
            "echo speed=4x",
            "echo progress=continue",
            "echo out_time_us=4000000",
            "echo progress=end",
            "echo some error text 1>&2",
            "exit /b 0"));
        var reports = new List<FfmpegProgress>();

        var result = await FfmpegRunner.RunAsync(
            new FfmpegTool(script, null, FfmpegVersion.Parse("ffmpeg version 8.0")),
            ["-i", "in.mp4", "out.mp4"],
            TimeSpan.FromSeconds(4),
            new SyncProgress(reports.Add));

        Assert.True(result.Succeeded);
        Assert.Contains("some error text", result.ErrorSummary, StringComparison.Ordinal);
        Assert.Equal(2, reports.Count);
        Assert.Equal(0.5, reports[0].Fraction);
        Assert.True(reports[1].Finished);
    }

    private sealed class SyncProgress(Action<FfmpegProgress> report) : IProgress<FfmpegProgress>
    {
        public void Report(FfmpegProgress value) => report(value);
    }
}

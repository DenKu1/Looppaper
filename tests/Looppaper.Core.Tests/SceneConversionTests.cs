using System.Text;
using Looppaper.Core.CommandLine;
using Looppaper.Core.Compression;
using Looppaper.Core.Tools;
using Looppaper.Core.WallpaperEngine;

namespace Looppaper.Core.Tests;

public class SceneConversionTests
{
    private const string LoopingScene = """
        {
          "general": { "orthogonalprojection": { "width": 3840, "height": 2160 } },
          "objects": [
            { "name": "a", "animation": { "options": { "mode": "loop", "fps": 30, "length": 300 } } },
            { "name": "b", "children": [ { "animation": { "options": { "mode": "loop", "fps": "30", "length": 450 } } } ] },
            { "name": "once", "animation": { "options": { "mode": "single", "fps": 30, "length": 7 } } },
            { "name": "zero", "animation": { "options": { "mode": "loop", "fps": 0, "length": 7 } } }
          ]
        }
        """;

    [Fact]
    public void ReadsSceneJsonFromAPackage()
    {
        var package = Package(("materials/x.json", "{}"), ("scene.json", LoopingScene));

        Assert.Equal(LoopingScene, SceneFiles.ReadFromPackage(new MemoryStream(package), "scene.json"));
        Assert.Null(SceneFiles.ReadFromPackage(new MemoryStream(package), "missing.json"));
        Assert.Null(SceneFiles.ReadFromPackage(new MemoryStream(package[..20]), "scene.json"));
        Assert.Null(SceneFiles.ReadFromPackage(new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF]), "scene.json"));
    }

    [Fact]
    public void ReadSceneJsonPrefersTheLooseFile()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "scene.pkg"), Package(("scene.json", "{\"from\":\"pkg\"}")));

        Assert.Equal("{\"from\":\"pkg\"}", SceneFiles.ReadSceneJson(dir.Path));
        File.WriteAllText(Path.Combine(dir.Path, "scene.json"), "{\"from\":\"loose\"}");
        Assert.Equal("{\"from\":\"loose\"}", SceneFiles.ReadSceneJson(dir.Path));
    }

    [Fact]
    public void SceneSizeAndLoop()
    {
        Assert.Equal((3840, 2160), SceneFiles.SceneSize(LoopingScene));
        Assert.Null(SceneFiles.SceneSize("{}"));
        Assert.Null(SceneFiles.SceneSize("not json"));

        // 300/30 = 10 s and 450/30 = 15 s loop together every 30 s.
        var loop = SceneFiles.SceneLoop(LoopingScene);
        Assert.Equal(30, loop?.Seconds);
    }

    [Theory]
    [InlineData("""{ "animation": { "options": { "mode": "loop", "fps": 25, "length": 12.5 } } }""", 0.5)]
    [InlineData("""[ { "animation": { "options": { "mode": "loop", "fps": 3, "length": 1 } } }, { "animation": { "options": { "mode": "loop", "fps": 2, "length": 1 } } } ]""", 1.0)]
    public void LoopUsesExactFractions(string json, double expected)
    {
        Assert.Equal(expected, SceneFiles.SceneLoop(json)!.Seconds, 9);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "animation": { "options": { "mode": "loop", "fps": 1, "length": 601 } } }""")]
    [InlineData("""{ "animation": "x" }""")]
    [InlineData("broken")]
    public void NoUsableLoop(string json)
    {
        Assert.Null(SceneFiles.SceneLoop(json));
    }

    [Theory]
    [InlineData("""<body><video autoplay loop muted src="clip.mp4"></video></body>""", "clip.mp4")]
    [InlineData("""<VIDEO LOOP><source src='v/a.mp4'></VIDEO>""", "v/a.mp4")]
    [InlineData("""<video src="clip.mp4"></video>""", null)]
    [InlineData("""<video loop src="a.mp4"></video><video loop src="b.mp4"></video>""", null)]
    [InlineData("""<canvas></canvas>""", null)]
    public void WebLoopVideo(string html, string? expected)
    {
        Assert.Equal(expected, SceneFiles.WebLoopVideo(html));
    }

    [Fact]
    public void LoopForWebItemUsesTheVideoLength()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "index.html"), """<video loop autoplay src="media/bg%20clip.mp4"></video>""");
        Directory.CreateDirectory(Path.Combine(dir.Path, "media"));
        File.WriteAllText(Path.Combine(dir.Path, "media", "bg clip.mp4"), "x");
        var item = new WorkshopItem(1, "9", dir.Path, "Page", WorkshopItemType.Web, Path.Combine(dir.Path, "index.html"), 1, DateTime.UnixEpoch, WorkshopItemStatus.NeedsConversion, null, null);
        string? probed = null;

        var loop = SceneFiles.LoopFor(item, path =>
        {
            probed = path;
            return TimeSpan.FromSeconds(12);
        });

        Assert.Equal(Path.Combine(dir.Path, "media", "bg clip.mp4"), probed);
        Assert.Equal(new LoopLength(12, "bg clip.mp4"), loop);
        Assert.Null(SceneFiles.LoopFor(item with { Type = WorkshopItemType.Application }, _ => TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void UserPositionFromWallpaperEngineConfig()
    {
        const string config = """
            {
              "?installdirectory": "C:/x",
              "Me": {
                "wproperties": {
                  "C:/Steam/steamapps/workshop/content/431960/42/project.json": {
                    "MonitorPositionL2560T0": { "alignmentposition": 10 },
                    "MonitorPositionL0T0": { "alignmentposition": 80 }
                  }
                }
              }
            }
            """;

        Assert.Equal(80, SceneFiles.UserPosition(config, @"C:\Steam\steamapps\workshop\content\431960\42"));
        Assert.Equal(50, SceneFiles.UserPosition(config, @"C:\Steam\steamapps\workshop\content\431960\43"));
        Assert.Equal(50, SceneFiles.UserPosition(null, @"C:\x"));
        Assert.Equal(50, SceneFiles.UserPosition("broken", @"C:\x"));
    }

    [Fact]
    public void GeometryCropsWiderAndTallerScenesAtThePosition()
    {
        var same = CaptureGeometry.For(2560, 1440, (3840, 2160), 50);
        var wide = CaptureGeometry.For(2560, 1440, (5760, 1080), 50);
        var wideLeft = CaptureGeometry.For(2560, 1440, (5760, 1080), 100);
        var tall = CaptureGeometry.For(1920, 1080, (1080, 1920), 50);
        var odd = CaptureGeometry.For(1365, 767, null, 50);

        Assert.Equal(new CaptureGeometry(2560, 1440, 2560, 1440, string.Empty), same);
        Assert.Equal(new CaptureGeometry(2560, 1440, 7680, 1440, ":crop_left=2560:crop_right=2560"), wide);
        Assert.Equal(":crop_left=0:crop_right=5120", wideLeft.Crop);
        Assert.Equal(3414, tall.RenderHeight);
        Assert.Equal(":crop_top=1167:crop_bottom=1167", tall.Crop);
        Assert.Equal((1364, 766), (odd.Width, odd.Height));
    }

    [Fact]
    public void PlanRecordsTheLoopOrCrossfadesThirtySeconds()
    {
        var looped = CapturePlan.From(new LoopLength(12, "x"));
        var none = CapturePlan.From(null);
        var tooLong = CapturePlan.From(new LoopLength(300, "x"));

        Assert.Equal((12.0, false, 12.0), (looped.Seconds, looped.Seamless, looped.RecordSeconds));
        Assert.Equal((30.0, true, 31.0), (none.Seconds, none.Seamless, none.RecordSeconds));
        Assert.True(tooLong.Seamless);
        Assert.Equal(0.5, SceneCapture.SeamlessFade(2));
        Assert.True(SceneCapture.Estimate(none) > TimeSpan.FromSeconds(31));
    }

    [Fact]
    public void EncoderProbingFallsBackToTheCpu()
    {
        var tried = new List<string>();

        var amf = SceneCapture.PickEncoder("h264", e =>
        {
            tried.Add(e);
            return e == "h264_amf";
        });

        Assert.Equal("h264_amf", amf);
        Assert.Equal(["h264_nvenc", "h264_amf"], tried);
        Assert.Equal("libx264", SceneCapture.PickEncoder("h264", _ => false));
        Assert.Equal("libx265", SceneCapture.PickEncoder("hevc", _ => false));
        Assert.Equal("h264", SceneCapture.Codec(2560, 1440, 60));
        Assert.Equal("hevc", SceneCapture.Codec(7680, 4320, 30));
        Assert.Contains("h264_qsv", SceneCapture.ProbeEncoderArgs("h264_qsv"));
    }

    [Fact]
    public void BitrateKeepsTheFileUnderTheLimit()
    {
        // A short 1440p30 clip is limited by quality, a long one by the 95 MB budget.
        Assert.Equal((long)(2560 * 1440 * 30 * CompressionPlanner.BitsPerPixel), SceneCapture.Bitrate(30, 2560, 1440, 30)); // same quality target as video compression
        var longClip = SceneCapture.Bitrate(120, 3840, 2160, 60);
        Assert.True(longClip * 120 / 8 <= SceneCapture.TargetBytes);
        Assert.True(SceneCapture.TargetBytes < SceneCapture.MaxBytes);
    }

    [Theory]
    [InlineData("h264_nvenc", "-rc", "vbr")]
    [InlineData("h264_amf", "-rc", "vbr_peak")]
    [InlineData("h264_qsv", "-pix_fmt", "nv12")]
    [InlineData("libx264", "-preset", "veryfast")]
    [InlineData("libx265", "-preset", "fast")]
    public void EncoderArgsCapTheBitrate(string encoder, string option, string value)
    {
        var args = SceneCapture.EncoderArgs(encoder, 8_000_000).ToList();

        Assert.Equal(value, args[args.IndexOf(option) + 1]);
        Assert.Equal("8000000", args[args.IndexOf("-maxrate") + 1]);
        Assert.Equal("16000000", args[args.IndexOf("-bufsize") + 1]);
    }

    [Fact]
    public void RecordArgsUseGfxcapture()
    {
        var geometry = new CaptureGeometry(2560, 1440, 3440, 1440, ":crop_left=440:crop_right=440");

        var nvenc = SceneCapture.RecordArgs(1234, geometry, 30, "h264_nvenc", 8_000_000, 31, "rec.mkv").ToList();
        var cpu = SceneCapture.RecordArgs(1234, geometry, 24, "libx264", 8_000_000, 12.5, "rec.mkv").ToList();

        Assert.Equal("gfxcapture=hwnd=1234:max_framerate=30:capture_cursor=0:crop_left=440:crop_right=440", nvenc[nvenc.IndexOf("-filter_complex") + 1]);
        Assert.Equal("31", nvenc[nvenc.IndexOf("-t") + 1]);
        Assert.Equal("rec.mkv", nvenc[^1]);
        Assert.EndsWith(",hwdownload,format=bgra", cpu[cpu.IndexOf("-filter_complex") + 1], StringComparison.Ordinal);
        Assert.Equal("12.5", cpu[cpu.IndexOf("-t") + 1]);
        Assert.Equal("24", cpu[cpu.IndexOf("-r") + 1]);
    }

    [Fact]
    public void SeamlessAndRemuxArgs()
    {
        var seamless = SceneCapture.SeamlessArgs("rec.mkv", 30, "hevc_nvenc", 8_000_000, "out.mp4").ToList();
        var remux = SceneCapture.RemuxArgs("rec.mkv", "h264_nvenc", "out.mp4").ToList();

        Assert.Equal(
            "[0:v]split[a][b];[a]trim=1:31,setpts=PTS-STARTPTS[a2];[b]trim=0:1,setpts=PTS-STARTPTS[b2];[a2][b2]xfade=transition=fade:duration=1:offset=29[v]",
            seamless[seamless.IndexOf("-filter_complex") + 1]);
        Assert.Equal("yuv420p", seamless[seamless.IndexOf("-pix_fmt") + 1]);
        Assert.Equal("hvc1", seamless[seamless.IndexOf("-tag:v") + 1]);
        Assert.Equal("out.mp4", seamless[^1]);
        Assert.Equal("copy", remux[remux.IndexOf("-c") + 1]);
        Assert.DoesNotContain("-tag:v", remux);
        Assert.Contains("+faststart", remux);
    }

    [Theory]
    [InlineData("frame=   10 fps=0.0\r\nframe=   88 fps=29\r\n", 88 / 3.0)]
    [InlineData("nothing", 0)]
    public void ParsesMeasuredFps(string stderr, double expected)
    {
        Assert.Equal(expected, SceneCapture.ParseMeasuredFps(stderr), 6);
    }

    [Theory]
    [InlineData(28, 30)]
    [InlineData(60, 30)]
    [InlineData(25, 24)]
    [InlineData(10, 24)]
    public void ChoosesAStandardFrameRate(double measured, int expected)
    {
        Assert.Equal(expected, SceneCapture.ChooseFps(measured));
    }

    [Fact]
    public void BlackFramesAreNotDrawn()
    {
        Assert.False(SceneCapture.IsDrawn([]));
        Assert.False(SceneCapture.IsDrawn(new byte[576]));
        var dim = new byte[576];
        dim[3] = 40;
        Assert.True(SceneCapture.IsDrawn(dim));
        Assert.Contains("scale=32:18", string.Join(" ", SceneCapture.ProbeDrawnArgs(7)), StringComparison.Ordinal);
    }

    [Fact]
    public void FfmpegProcessCollectsOutputAndTimesOut()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var fast = Path.Combine(dir.Path, "fast.cmd");
        File.WriteAllText(fast, "@echo off\r\necho frame=   90 1>&2\r\necho hello\r\nexit /b 3");
        var slow = Path.Combine(dir.Path, "slow.cmd");
        File.WriteAllText(slow, "@echo off\r\nping -n 30 127.0.0.1 >nul");

        var result = FfmpegProcess.Run(fast, ["-x"], TimeSpan.FromSeconds(20));
        var timedOut = FfmpegProcess.Run(slow, [], TimeSpan.FromMilliseconds(500));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("hello", Encoding.ASCII.GetString(result.StandardOutput).Trim());
        Assert.Equal(30, SceneCapture.ParseMeasuredFps(result.StandardError));
        Assert.True(timedOut.TimedOut);
        Assert.False(timedOut.Succeeded);
    }

    [Fact]
    public void ParsesWeConvert()
    {
        var options = CommandLineParser.Parse(["--we-convert=1234567890"]);

        Assert.Empty(options.Errors);
        Assert.Equal("1234567890", options.WeConvert);
        Assert.True(options.IsWallpaperEngineCommand);
        Assert.NotEmpty(CommandLineParser.Parse(["--we-convert", "1", "--we-set", "1"]).Errors);
    }

    /// <summary>Builds a Wallpaper Engine .pkg: version string, entry table, then the file data.</summary>
    private static byte[] Package(params (string Name, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        void WriteString(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }

        WriteString("PKGV0019");
        writer.Write((uint)files.Length);
        uint offset = 0;
        foreach (var (name, content) in files)
        {
            var size = (uint)Encoding.UTF8.GetByteCount(content);
            WriteString(name);
            writer.Write(offset);
            writer.Write(size);
            offset += size;
        }

        foreach (var (_, content) in files)
        {
            writer.Write(Encoding.UTF8.GetBytes(content));
        }

        writer.Flush();
        return stream.ToArray();
    }
}

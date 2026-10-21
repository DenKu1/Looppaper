using Looppaper.Core.Compression;
using Looppaper.Core.Media;
using Looppaper.Core.Tools;

namespace Looppaper.Core.Tests;

public class VideoCompressorTests
{
    private const long Mb = MediaScanner.BytesPerMb;

    private static readonly MediaInfo FourK = new(TimeSpan.FromSeconds(112), 3840, 2160, "h264", 30, true, 16_000_000, 228 * Mb);

    /// <summary>Stands in for ffmpeg: records every run and writes an output file of the next queued size.</summary>
    private sealed class FakeFfmpeg : IFfmpegExecutor
    {
        public List<IReadOnlyList<string>> Runs { get; } = [];

        public HashSet<string> BrokenEncoders { get; } = [];

        public HashSet<string> FailingEncodes { get; } = [];

        public Queue<long> OutputSizes { get; } = new();

        public MediaInfo? ResultInfo { get; set; } = new(TimeSpan.FromSeconds(112), 2560, 1440, "h264", 30, true, null, null);

        public CancellationTokenSource? CancelOnEncode { get; set; }

        public IEnumerable<IReadOnlyList<string>> Encodes => Runs.Where(r => !r.Contains("lavfi"));

        public MediaInfo? Probe(string file) => File.Exists(file) ? ResultInfo : null;

        public Task<FfmpegResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan? totalDuration, IProgress<FfmpegProgress>? progress, CancellationToken cancellationToken)
        {
            Runs.Add(arguments);
            var encoder = arguments[IndexOf(arguments, "-c:v") + 1];
            if (arguments.Contains("lavfi"))
            {
                return Task.FromResult(new FfmpegResult(BrokenEncoders.Contains(encoder) ? 1 : 0, ["probe"], false));
            }

            var output = arguments[^1];
            File.WriteAllBytes(output, []);
            if (CancelOnEncode is { } cancel)
            {
                cancel.Cancel();
                return Task.FromResult(new FfmpegResult(-1, [], true));
            }

            if (FailingEncodes.Contains(encoder))
            {
                return Task.FromResult(new FfmpegResult(1, ["Error while opening encoder"], false));
            }

            using (var stream = new FileStream(output, FileMode.Create))
            {
                stream.SetLength(OutputSizes.Count > 0 ? OutputSizes.Dequeue() : 90 * Mb);
            }

            progress?.Report(new FfmpegProgress(totalDuration ?? TimeSpan.Zero, 2, 1, true));
            return Task.FromResult(new FfmpegResult(0, [], false));
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == item)
            {
                return i;
            }
        }

        return -1;
    }

    private static string ArgAfter(IReadOnlyList<string> args, string name) => args[IndexOf(args, name) + 1];

    private static (CompressionRequest Request, string Original, string Cache) Setup(TempDir dir, MediaInfo? info = null)
    {
        var original = dir.File("Floating In Space.mp4", 2048);
        var key = VideoFileKey.TryFromFile(original)! with { SizeBytes = 228 * Mb };
        var cache = Path.Combine(dir.Path, "cache");
        return (new CompressionRequest(key, info ?? FourK, cache, new PixelSize(2560, 1440)), original, cache);
    }

    [Fact]
    public async Task CompressesIntoTheCacheWithTheFirstWorkingHardwareEncoder()
    {
        using var dir = new TempDir();
        var (request, original, cache) = Setup(dir);
        var ffmpeg = new FakeFfmpeg { BrokenEncoders = { "h264_nvenc" } };
        var statuses = new List<string>();
        var compressor = new VideoCompressor(ffmpeg, new MemoryLog());
        compressor.Status += statuses.Add;

        var result = await compressor.CompressAsync(request, null, CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Path.Combine(cache, VideoCompressor.OutputFileName(request.Source)), result.OutputPath);
        Assert.StartsWith("Floating In Space-", Path.GetFileName(result.OutputPath), StringComparison.Ordinal);
        Assert.Equal(90 * Mb, new FileInfo(result.OutputPath!).Length);
        Assert.Equal(2048, new FileInfo(original).Length); // original untouched
        Assert.Empty(Directory.GetFiles(cache, "*.part"));
        Assert.Contains("Encoder: AMD AMF", statuses);

        var encode = Assert.Single(ffmpeg.Encodes);
        Assert.Equal("h264_amf", ArgAfter(encode, "-c:v"));
        Assert.Equal(original, ArgAfter(encode, "-i"));
        Assert.Equal("scale=2560:1440:flags=lanczos", ArgAfter(encode, "-vf"));
        Assert.EndsWith(".part", encode[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FallsBackToTheCpuWhenNoHardwareEncoderWorks()
    {
        using var dir = new TempDir();
        var (request, _, _) = Setup(dir);
        var ffmpeg = new FakeFfmpeg { BrokenEncoders = { "h264_nvenc", "h264_amf", "h264_qsv" } };

        var result = await new VideoCompressor(ffmpeg, new MemoryLog()).CompressAsync(request, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, ffmpeg.Runs.Count(r => r.Contains("lavfi")));
        Assert.Equal("libx264", ArgAfter(Assert.Single(ffmpeg.Encodes), "-c:v"));
    }

    [Fact]
    public async Task HardwareEncodeFailureIsRetriedOnTheCpu()
    {
        using var dir = new TempDir();
        var (request, _, _) = Setup(dir);
        var ffmpeg = new FakeFfmpeg { FailingEncodes = { "h264_nvenc" } };

        var result = await new VideoCompressor(ffmpeg, new MemoryLog()).CompressAsync(request, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["h264_nvenc", "libx264"], ffmpeg.Encodes.Select(e => ArgAfter(e, "-c:v")));
    }

    [Fact]
    public async Task ResultOverHundredMbStillSucceedsButIsFlaggedLarge()
    {
        using var dir = new TempDir();
        var (request, _, _) = Setup(dir);
        var ffmpeg = new FakeFfmpeg();
        ffmpeg.OutputSizes.Enqueue(150 * Mb);

        var result = await new VideoCompressor(ffmpeg, new MemoryLog()).CompressAsync(request, null, CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.IsLarge);
        Assert.Single(ffmpeg.Encodes); // no retry, no refusal
    }

    [Fact]
    public async Task CompactOriginalIsKeptWithoutEncoding()
    {
        using var dir = new TempDir();
        var compact = new MediaInfo(TimeSpan.FromSeconds(60), 1920, 1080, "h264", 30, true, 4_000_000, 30 * Mb);
        var (request, _, _) = Setup(dir, compact);
        request = request with { Source = request.Source with { SizeBytes = 30 * Mb } };
        var ffmpeg = new FakeFfmpeg();

        var result = await new VideoCompressor(ffmpeg, new MemoryLog()).CompressAsync(request, null, CancellationToken.None);

        Assert.True(result.KeepOriginal);
        Assert.False(result.Succeeded);
        Assert.Empty(ffmpeg.Runs);
    }

    [Fact]
    public async Task ResultThatIsNotSmallerIsDiscardedAndTheOriginalKept()
    {
        using var dir = new TempDir();
        var hd = new MediaInfo(TimeSpan.FromSeconds(112), 1920, 1080, "h264", 30, true, 16_000_000, 228 * Mb);
        var (request, _, cache) = Setup(dir, hd);
        var ffmpeg = new FakeFfmpeg();
        ffmpeg.OutputSizes.Enqueue(230 * Mb);

        var result = await new VideoCompressor(ffmpeg, new MemoryLog()).CompressAsync(request, null, CancellationToken.None);

        Assert.True(result.KeepOriginal);
        Assert.Empty(Directory.GetFiles(cache));
    }
    [Fact]
    public async Task ResultThatCannotBeReadBackIsDeleted()
    {
        using var dir = new TempDir();
        var (request, _, cache) = Setup(dir);
        var ffmpeg = new FakeFfmpeg { ResultInfo = null };

        var result = await new VideoCompressor(ffmpeg, new MemoryLog()).CompressAsync(request, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("read back", result.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(cache));
    }

    [Fact]
    public async Task CancellingDeletesThePartialFile()
    {
        using var dir = new TempDir();
        var (request, original, cache) = Setup(dir);
        using var cancel = new CancellationTokenSource();
        var ffmpeg = new FakeFfmpeg { CancelOnEncode = cancel };

        var result = await new VideoCompressor(ffmpeg, new MemoryLog()).CompressAsync(request, null, cancel.Token);

        Assert.False(result.Succeeded);
        Assert.True(result.Cancelled);
        Assert.Empty(Directory.GetFiles(cache));
        Assert.True(File.Exists(original));
    }

    [Fact]
    public async Task UnplannableVideoIsNotEncoded()
    {
        using var dir = new TempDir();
        var (request, _, _) = Setup(dir, FourK with { Duration = null });
        var ffmpeg = new FakeFfmpeg();

        var result = await new VideoCompressor(ffmpeg, new MemoryLog()).CompressAsync(request, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(ffmpeg.Runs);
    }

    [Fact]
    public void EncodeArgumentsKeepAudioAndSkipScalingWhenNotNeeded()
    {
        var plan = new CompressionPlan(new PixelSize(1920, 1080), new PixelSize(1920, 1080), 6000, 128, TimeSpan.FromSeconds(60), 0, Worthwhile: true);

        var args = CompressionArguments.Encode(@"C:\in.mp4", @"C:\out.mp4.part", plan, VideoEncoder.X264);

        Assert.DoesNotContain("-vf", args);
        Assert.Contains("0:a:0?", args);
        Assert.Equal("aac", ArgAfter(args, "-c:a"));
        Assert.Equal("128k", ArgAfter(args, "-b:a"));
        Assert.Equal("6000k", ArgAfter(args, "-b:v"));
        Assert.Equal("9000k", ArgAfter(args, "-maxrate"));
        Assert.Equal("12000k", ArgAfter(args, "-bufsize"));
        Assert.Equal("yuv420p", ArgAfter(args, "-pix_fmt"));
        Assert.Equal("mp4", ArgAfter(args, "-f"));
        Assert.Equal(@"C:\out.mp4.part", args[^1]);
        Assert.DoesNotContain("-an", args);
    }

    [Fact]
    public void EncodeArgumentsHaveNoAudioForASilentVideo()
    {
        var plan = new CompressionPlan(new PixelSize(1920, 1080), new PixelSize(1920, 1080), 420, 0, TimeSpan.FromSeconds(1700), 0, Worthwhile: true);

        var args = CompressionArguments.Encode(@"C:\in.mp4", @"C:\out.mp4", plan, VideoEncoder.Qsv);

        Assert.Contains("-an", args);
        Assert.DoesNotContain("0:a:0?", args);
        Assert.DoesNotContain("-c:a", args);
        Assert.Equal("h264_qsv", ArgAfter(args, "-c:v"));
        Assert.Equal("nv12", ArgAfter(args, "-pix_fmt"));
    }

    [Fact]
    public void ProbeArgumentsEncodeAGeneratedClipToNowhere()
    {
        var args = CompressionArguments.ProbeEncoder(VideoEncoder.Nvenc);

        Assert.Equal("lavfi", ArgAfter(args, "-f"));
        Assert.Equal("h264_nvenc", ArgAfter(args, "-c:v"));
        Assert.Equal("-", args[^1]);
        Assert.Equal("null", args[^2]);
    }

    [Fact]
    public void OutputNameIsSafeAndStable()
    {
        var key = new VideoFileKey(@"C:\v\my clip.MP4", 5, 6);

        Assert.Equal($"my clip-{key.ShortHash()}.mp4", VideoCompressor.OutputFileName(key));
        Assert.Equal(VideoCompressor.OutputFileName(key), VideoCompressor.OutputFileName(key with { FullPath = @"c:\V\MY CLIP.mp4" }).Replace("MY CLIP", "my clip", StringComparison.Ordinal));
    }
}

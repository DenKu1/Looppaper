using Looppaper.Core.CommandLine;
using Looppaper.Core.Compression;

namespace Looppaper.Core.Tests;

public class CompressionStoreTests
{
    private static readonly VideoFileKey Video = new(@"C:\Videos\big.mp4", 228_000_000, 638_000_000_000_000_000);

    [Fact]
    public void RemembersNoPerFileVersion()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "sub", "compression.json");
        var store = new CompressionStore(file, new MemoryLog());

        Assert.False(store.IsDeclined(Video));
        store.SetDeclined(Video, true);

        var reopened = new CompressionStore(file, new MemoryLog());
        Assert.True(reopened.IsDeclined(Video));
        Assert.True(reopened.IsDeclined(Video with { FullPath = @"c:\videos\BIG.mp4" }));
        Assert.False(reopened.IsDeclined(Video with { SizeBytes = 1 }));
        Assert.False(reopened.IsDeclined(Video with { LastWriteUtcTicks = 1 }));
        Assert.False(reopened.IsDeclined(Video with { FullPath = @"C:\Videos\other.mp4" }));
    }

    [Fact]
    public void YesClearsAnEarlierNo()
    {
        using var dir = new TempDir();
        var store = new CompressionStore(Path.Combine(dir.Path, "c.json"), new MemoryLog());

        store.SetDeclined(Video, true);
        store.SetDeclined(Video, false);

        Assert.False(store.IsDeclined(Video));
    }

    [Fact]
    public void NewVersionOfTheFileReplacesTheOldEntry()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "c.json");
        var store = new CompressionStore(file, new MemoryLog());
        store.SetDeclined(Video, true);

        var edited = Video with { LastWriteUtcTicks = Video.LastWriteUtcTicks + 10 };
        store.SetToldFfmpegMissing(edited);

        Assert.False(store.IsDeclined(Video));
        Assert.True(store.WasToldFfmpegMissing(edited));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"path\""));
    }

    [Fact]
    public void FfmpegMissingNoticeIsSeparateFromNo()
    {
        using var dir = new TempDir();
        var store = new CompressionStore(Path.Combine(dir.Path, "c.json"), new MemoryLog());

        store.SetToldFfmpegMissing(Video);

        Assert.True(store.WasToldFfmpegMissing(Video));
        Assert.False(store.IsDeclined(Video));
    }

    [Fact]
    public void RecordsTheCompressedCopy()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "c.json");
        var copy = dir.File("big-0123456789.mp4", 90);
        var store = new CompressionStore(file, new MemoryLog());
        store.SetDeclined(Video, true);

        store.RecordCompressed(Video, copy);

        var reopened = new CompressionStore(file, new MemoryLog());
        Assert.Equal(copy, reopened.FindCompressed(Video, 100));
        Assert.False(reopened.IsDeclined(Video));
        Assert.Null(reopened.FindCompressed(Video with { SizeBytes = 5 }, 100));
    }

    [Fact]
    public void ForgetsACopyThatIsGoneOrTooLarge()
    {
        using var dir = new TempDir();
        var store = new CompressionStore(Path.Combine(dir.Path, "c.json"), new MemoryLog());
        var copy = dir.File("copy.mp4", 200);
        store.RecordCompressed(Video, copy);

        Assert.Null(store.FindCompressed(Video, 100)); // larger than the (lowered) limit
        File.WriteAllBytes(copy, new byte[50]);
        Assert.Null(store.FindCompressed(Video, 100)); // the record was dropped

        store.RecordCompressed(Video, copy);
        File.Delete(copy);
        var log = new MemoryLog();
        Assert.Null(new CompressionStore(store.FilePath, log).FindCompressed(Video, 100));
        Assert.Contains(log.Warnings, w => w.Contains("is gone", StringComparison.Ordinal));
    }

    [Fact]
    public void CompressedCopyIsPlayedInsteadOfTheOriginal()
    {
        using var dir = new TempDir();
        var store = new CompressionStore(Path.Combine(dir.Path, "c.json"), new MemoryLog());
        var copy = dir.File("copy.mp4", 1024);
        store.RecordCompressed(Video, copy);

        var choice = VideoChooser.Choose([Video], 100, k => store.FindCompressed(k, VideoSizeLimit.Bytes(100)), new MemoryLog());

        Assert.Equal(copy, choice.PlayPath);
        Assert.Null(choice.Oversized);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"entries\": 5}")]
    [InlineData("")]
    public void BrokenFileCountsAsEmpty(string content)
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "c.json");
        File.WriteAllText(file, content);
        var log = new MemoryLog();
        var store = new CompressionStore(file, log);

        Assert.False(store.IsDeclined(Video));
        store.SetDeclined(Video, true);
        Assert.True(store.IsDeclined(Video));
    }

    [Theory]
    [InlineData("y", true)]
    [InlineData(" YES ", true)]
    [InlineData("Y", true)]
    [InlineData("n", false)]
    [InlineData("", false)]
    [InlineData("yep", false)]
    [InlineData(null, false)]
    public void OnlyYesMeansYes(string? answer, bool expected)
    {
        Assert.Equal(expected, CompressionPrompt.IsYes(answer));
    }

    [Fact]
    public void PromptNamesFileSizeEstimateAndLimit()
    {
        var plan = new CompressionPlan(new PixelSize(3840, 2160), new PixelSize(2560, 1440), 11_000, 128, false, TimeSpan.FromSeconds(65), 92 * 1024 * 1024);

        var details = CompressionPrompt.Details(Video, 100, plan, @"C:\cache");

        Assert.Contains("big.mp4 is 217.4 MB, above the 100 MB video limit.", details, StringComparison.Ordinal);
        Assert.Contains("Estimated result: about 92.0 MB", details, StringComparison.Ordinal);
        Assert.Contains("The original is kept", details, StringComparison.Ordinal);
        Assert.Equal("Compress to fit under 100 MB? [y/N] ", CompressionPrompt.ConsoleQuestion(100));
        Assert.Contains("ffmpeg", CompressionPrompt.FfmpegMissingText(Video, 100, "install hint"), StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesCompressCommand()
    {
        var options = CommandLineParser.Parse(["--compress", @"D:\clips\big.mp4"]);

        Assert.Empty(options.Errors);
        Assert.Equal(@"D:\clips\big.mp4", options.Compress);
        Assert.True(options.IsCommand);
        Assert.NotEmpty(CommandLineParser.Parse(["--compress"]).Errors);
        Assert.Contains("--compress <file>", CommandLineOptions.HelpText, StringComparison.Ordinal);
    }
}

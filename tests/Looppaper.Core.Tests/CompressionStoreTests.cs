using Looppaper.Core.CommandLine;
using Looppaper.Core.Compression;

namespace Looppaper.Core.Tests;

public class CompressionStoreTests
{
    private static readonly VideoFileKey Video = new(@"C:\Videos\big.mp4", 228_000_000, 638_000_000_000_000_000);

    [Fact]
    public void RemembersKeepingTheOriginalPerFileVersion()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "sub", "compression.json");
        var store = new CompressionStore(file, new MemoryLog());

        Assert.False(store.IsKeptAsIs(Video));
        store.RecordKeepOriginal(Video);

        var reopened = new CompressionStore(file, new MemoryLog());
        Assert.True(reopened.IsKeptAsIs(Video));
        Assert.True(reopened.IsKeptAsIs(Video with { FullPath = @"c:\videos\BIG.mp4" }));
        Assert.False(reopened.IsKeptAsIs(Video with { SizeBytes = 1 }));
        Assert.False(reopened.IsKeptAsIs(Video with { LastWriteUtcTicks = 1 }));
        Assert.False(reopened.IsKeptAsIs(Video with { FullPath = @"C:\Videos\other.mp4" }));
    }

    [Fact]
    public void NewVersionOfTheFileReplacesTheOldEntry()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "c.json");
        var store = new CompressionStore(file, new MemoryLog());
        store.RecordKeepOriginal(Video);

        var edited = Video with { LastWriteUtcTicks = Video.LastWriteUtcTicks + 10 };
        store.SetToldFfmpegMissing(edited);

        Assert.False(store.IsKeptAsIs(Video));
        Assert.True(store.WasToldFfmpegMissing(edited));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"path\""));
    }

    [Fact]
    public void RecordsTheCompressedCopyOfAnySize()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "c.json");
        var copy = dir.File("big-0123456789.mp4", 90);
        var store = new CompressionStore(file, new MemoryLog());
        store.RecordKeepOriginal(Video);

        store.RecordCompressed(Video, copy);

        var reopened = new CompressionStore(file, new MemoryLog());
        Assert.Equal(copy, reopened.FindCompressed(Video));
        Assert.False(reopened.IsKeptAsIs(Video));
        Assert.Null(reopened.FindCompressed(Video with { SizeBytes = 5 }));
    }

    [Fact]
    public void ForgetsACopyThatIsGone()
    {
        using var dir = new TempDir();
        var store = new CompressionStore(Path.Combine(dir.Path, "c.json"), new MemoryLog());
        var copy = dir.File("copy.mp4", 200);
        store.RecordCompressed(Video, copy);
        File.Delete(copy);
        var log = new MemoryLog();

        Assert.Null(new CompressionStore(store.FilePath, log).FindCompressed(Video));
        Assert.Contains(log.Warnings, w => w.Contains("is gone", StringComparison.Ordinal));
        Assert.Null(store.FindCompressed(Video)); // the record was dropped
    }

    [Fact]
    public void OldRecordsWithARememberedNoStillLoad()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "c.json");
        var copy = dir.File("copy.mp4", 10);
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1,
            entries = new[] { new { path = Video.FullPath, size = Video.SizeBytes, lastWriteUtcTicks = Video.LastWriteUtcTicks, declined = true, compressed = copy } },
        }));

        Assert.Equal(copy, new CompressionStore(file, new MemoryLog()).FindCompressed(Video));
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
        var store = new CompressionStore(file, new MemoryLog());

        Assert.False(store.IsKeptAsIs(Video));
        store.RecordKeepOriginal(Video);
        Assert.True(store.IsKeptAsIs(Video));
    }

    [Fact]
    public void MessagesTellRatherThanAsk()
    {
        var plan = new CompressionPlan(new PixelSize(3840, 2160), new PixelSize(2560, 1440), 11_000, 128, TimeSpan.FromSeconds(65), 92 * 1024 * 1024, Worthwhile: true);

        var starting = CompressionMessages.Starting(Video, plan, @"C:\cache");

        Assert.Contains("Compressing big.mp4 (217.4 MB)", starting, StringComparison.Ordinal);
        Assert.Contains("about 92.0 MB", starting, StringComparison.Ordinal);
        Assert.Contains("The original is kept", starting, StringComparison.Ordinal);
        Assert.DoesNotContain("?", starting, StringComparison.Ordinal);
        Assert.Contains("in the background", CompressionMessages.StartingInBackground(Video), StringComparison.Ordinal);
        Assert.Contains("plays uncompressed", CompressionMessages.FfmpegMissing(Video, "install hint"), StringComparison.Ordinal);
    }

    [Fact]
    public void LargeResultGetsAPerformanceWarning()
    {
        var warning = CompressionMessages.LargeWarning("big.mp4", 150 * 1024 * 1024);

        Assert.Contains("still 150.0 MB", warning, StringComparison.Ordinal);
        Assert.Contains("slow your PC down", warning, StringComparison.Ordinal);
        Assert.Contains("plays anyway", warning, StringComparison.Ordinal);
        Assert.True(VideoSize.IsLarge(VideoSize.WarningBytes + 1));
        Assert.False(VideoSize.IsLarge(VideoSize.WarningBytes));
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

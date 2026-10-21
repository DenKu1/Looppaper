using Looppaper.Core.Compression;
using Looppaper.Core.Media;

namespace Looppaper.Core.Tests;

public class VideoChooserTests
{
    private const long Mb = MediaScanner.BytesPerMb;

    private static VideoFileKey Key(string path, long sizeMb) => new(path, sizeMb * Mb, 1000);

    [Fact]
    public void PlaysFirstVideoWithinTheLimit()
    {
        var choice = VideoChooser.Choose([Key(@"C:\v\a.mp4", 40), Key(@"C:\v\b.mp4", 20)], 100, _ => null, new MemoryLog());

        Assert.Equal(@"C:\v\a.mp4", choice.PlayPath);
        Assert.Null(choice.Oversized);
    }

    [Fact]
    public void SkipsOversizedVideoAndRemembersIt()
    {
        var log = new MemoryLog();
        var big = Key(@"C:\v\big.mp4", 218);

        var choice = VideoChooser.Choose([big, Key(@"C:\v\small.mp4", 50)], 100, _ => null, log);

        Assert.Equal(@"C:\v\small.mp4", choice.PlayPath);
        Assert.Same(big, choice.Oversized);
        Assert.Contains(log.Warnings, w => w.Contains("218.0 MB, above the 100 MB video limit", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyOversizedVideosMeansPictures()
    {
        var choice = VideoChooser.Choose([Key(@"C:\v\big.mp4", 101)], 100, _ => null, new MemoryLog());

        Assert.Null(choice.PlayPath);
        Assert.Equal(@"C:\v\big.mp4", choice.Oversized?.FullPath);
    }

    [Fact]
    public void UsesTheConfiguredLimit()
    {
        var choice = VideoChooser.Choose([Key(@"C:\v\a.mp4", 60)], 50, _ => null, new MemoryLog());

        Assert.Null(choice.PlayPath);
    }

    [Fact]
    public void PlaysCompressedCopyOfOversizedVideo()
    {
        var big = Key(@"C:\v\big.mp4", 300);

        var choice = VideoChooser.Choose([big, Key(@"C:\v\small.mp4", 5)], 100, k => k == big ? @"C:\cache\big-1.mp4" : null, new MemoryLog());

        Assert.Equal(@"C:\cache\big-1.mp4", choice.PlayPath);
        Assert.Null(choice.Oversized);
    }

    [Fact]
    public void SkipsDuplicatesAndEmptyFiles()
    {
        var choice = VideoChooser.Choose(
            [new VideoFileKey(@"C:\v\empty.mp4", 0, 1), Key(@"C:\v\a.mp4", 200), Key(@"c:\V\A.MP4", 200), Key(@"C:\v\b.mp4", 1)],
            100,
            _ => null,
            new MemoryLog());

        Assert.Equal(@"C:\v\b.mp4", choice.PlayPath);
        Assert.Equal(@"C:\v\a.mp4", choice.Oversized?.FullPath);
    }

    [Fact]
    public void ExactlyAtTheLimitIsAllowed()
    {
        Assert.False(VideoSizeLimit.IsOversized(100 * Mb, 100));
        Assert.True(VideoSizeLimit.IsOversized((100 * Mb) + 1, 100));
    }

    [Fact]
    public void FileKeyReadsFileAndChangesWithIt()
    {
        using var dir = new TempDir();
        var path = dir.File("clip.mp4", 1234);
        var key = VideoFileKey.TryFromFile(path);

        Assert.NotNull(key);
        Assert.Equal(1234, key.SizeBytes);
        Assert.True(key.Matches(key with { FullPath = path.ToUpperInvariant() }));
        Assert.False(key.Matches(key with { LastWriteUtcTicks = key.LastWriteUtcTicks + 1 }));
        Assert.Equal(key.ShortHash(), (key with { FullPath = path.ToUpperInvariant() }).ShortHash());
        Assert.NotEqual(key.ShortHash(), (key with { SizeBytes = 1 }).ShortHash());
        Assert.Equal(10, key.ShortHash().Length);
        Assert.Null(VideoFileKey.TryFromFile(Path.Combine(dir.Path, "missing.mp4")));
    }
}

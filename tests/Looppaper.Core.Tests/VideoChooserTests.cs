using Looppaper.Core.Compression;
using Looppaper.Core.Media;

namespace Looppaper.Core.Tests;

public class VideoChooserTests
{
    private const long Mb = MediaScanner.BytesPerMb;

    private static VideoFileKey Key(string path, long sizeMb) => new(path, sizeMb * Mb, 1000);

    [Fact]
    public void PlaysTheFirstVideoAndCompressesItWhenThereIsNoCopyYet()
    {
        var first = Key(@"C:\v\big.mp4", 218);

        var choice = VideoChooser.Choose([first, Key(@"C:\v\small.mp4", 20)], _ => null, compress: true, new MemoryLog());

        Assert.Equal(@"C:\v\big.mp4", choice.PlayPath); // plays meanwhile: nothing is refused
        Assert.Same(first, choice.ToCompress);
    }

    [Fact]
    public void SmallVideosAreCompressedToo()
    {
        var choice = VideoChooser.Choose([Key(@"C:\v\small.mp4", 5)], _ => null, compress: true, new MemoryLog());

        Assert.Equal(@"C:\v\small.mp4", choice.ToCompress?.FullPath);
    }

    [Fact]
    public void PlaysTheCompressedCopyWhenThereIsOne()
    {
        var big = Key(@"C:\v\big.mp4", 300);

        var choice = VideoChooser.Choose([big], k => k == big ? @"C:\cache\big-1.mp4" : null, compress: true, new MemoryLog());

        Assert.Equal(@"C:\cache\big-1.mp4", choice.PlayPath);
        Assert.Null(choice.ToCompress);
    }

    [Fact]
    public void OriginalKeptAsIsIsNotCompressedAgain()
    {
        var compact = Key(@"C:\v\compact.mp4", 10);

        var choice = VideoChooser.Choose([compact], k => k.FullPath, compress: true, new MemoryLog());

        Assert.Equal(@"C:\v\compact.mp4", choice.PlayPath);
        Assert.Null(choice.ToCompress);
    }

    [Fact]
    public void CompressionOffPlaysTheOriginal()
    {
        var choice = VideoChooser.Choose([Key(@"C:\v\big.mp4", 300)], _ => @"C:\cache\copy.mp4", compress: false, new MemoryLog());

        Assert.Equal(@"C:\v\big.mp4", choice.PlayPath);
        Assert.Null(choice.ToCompress);
    }

    [Fact]
    public void SkipsEmptyFilesAndReturnsNothingForNoVideos()
    {
        var choice = VideoChooser.Choose([new VideoFileKey(@"C:\v\empty.mp4", 0, 1), Key(@"C:\v\b.mp4", 1)], _ => null, compress: true, new MemoryLog());

        Assert.Equal(@"C:\v\b.mp4", choice.PlayPath);
        Assert.Null(VideoChooser.Choose([], _ => null, compress: true, new MemoryLog()).PlayPath);
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

using Looppaper.Core.Media;

namespace Looppaper.Core.Tests;

public class MediaScannerTests
{
    [Fact]
    public void FindsPicturesCaseInsensitivelyInStableOrder()
    {
        using var dir = new TempDir();
        dir.File("b.PNG");
        dir.File("A.jpg");
        dir.File("c.JpEg");
        dir.File("notes.txt");
        dir.File("clip.mp4");
        dir.File("pic.gif");
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        File.WriteAllText(Path.Combine(dir.Path, "sub", "deep.jpg"), "x");

        var pictures = new MediaScanner(new MemoryLog()).Scan(dir.Path, MediaKind.Picture, 50);

        Assert.Equal(["A.jpg", "b.PNG", "c.JpEg"], pictures.Select(Path.GetFileName));
    }

    [Fact]
    public void FindsVideos()
    {
        using var dir = new TempDir();
        dir.File("2.MP4");
        dir.File("1.mp4");
        dir.File("song.mp3");
        dir.File("cover.jpg");

        var videos = new MediaScanner(new MemoryLog()).Scan(dir.Path, MediaKind.Video, 100);

        Assert.Equal(["1.mp4", "2.MP4"], videos.Select(Path.GetFileName));
    }

    [Fact]
    public void SkipsOversizeAndEmptyFilesAndLogsOnce()
    {
        using var dir = new TempDir();
        dir.File("ok.mp4", MediaScanner.BytesPerMb);
        dir.File("big.mp4", (2 * MediaScanner.BytesPerMb) + 1);
        dir.File("empty.mp4", 0);
        var log = new MemoryLog();
        var scanner = new MediaScanner(log);

        var first = scanner.Scan(dir.Path, MediaKind.Video, 2);
        var second = scanner.Scan(dir.Path, MediaKind.Video, 2);

        Assert.Equal(["ok.mp4"], first.Select(Path.GetFileName));
        Assert.Equal(first, second);
        Assert.Equal(2, log.Warnings.Count());
        Assert.Contains(log.Warnings, w => w.Contains("big.mp4", StringComparison.Ordinal) && w.Contains("2 MB", StringComparison.Ordinal));
    }

    [Fact]
    public void FileExactlyAtLimitIsAccepted()
    {
        using var dir = new TempDir();
        dir.File("edge.png", 3 * MediaScanner.BytesPerMb);

        Assert.Single(new MediaScanner(new MemoryLog()).Scan(dir.Path, MediaKind.Picture, 3));
    }

    [Fact]
    public void MissingFolderReturnsEmptyAndWarnsOnce()
    {
        var log = new MemoryLog();
        var scanner = new MediaScanner(log);
        var missing = Path.Combine(Path.GetTempPath(), "looppaper-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Empty(scanner.Scan(missing, MediaKind.Picture, 50));
        Assert.Empty(scanner.Scan(missing, MediaKind.Picture, 50));
        Assert.Single(log.Warnings);
    }

    [Theory]
    [InlineData("x.JPG", MediaKind.Picture, true)]
    [InlineData("x.jpeg", MediaKind.Picture, true)]
    [InlineData("x.png", MediaKind.Video, false)]
    [InlineData("x.Mp4", MediaKind.Video, true)]
    [InlineData("x.mp3", MediaKind.Video, false)]
    [InlineData("x.bmp", MediaKind.Picture, false)]
    [InlineData("jpg", MediaKind.Picture, false)]
    public void IsSupported(string path, MediaKind kind, bool expected)
    {
        Assert.Equal(expected, MediaScanner.IsSupported(path, kind));
    }
}

using Looppaper.Core.CommandLine;
using Looppaper.Core.Media;
using Looppaper.Core.WallpaperEngine;

namespace Looppaper.Core.Tests;

public class WorkshopLibraryTests
{
    private const long Mb = MediaScanner.BytesPerMb;

    [Fact]
    public void ProjectFileReadsFieldsWhateverTheirCase()
    {
        const string json = """
            // exported by Wallpaper Engine
            {
              "Title": "  Floating In Space ",
              "type": "Video",
              "FILE": "space.mp4",
              "preview": "preview.gif",
              "general": { "properties": {} },
            }
            """;

        var project = ProjectFile.Parse("\uFEFF" + json);

        Assert.NotNull(project);
        Assert.Equal("Floating In Space", project.Title);
        Assert.Equal(WorkshopItemType.Video, project.Type);
        Assert.Equal("space.mp4", project.File);
        Assert.Equal("preview.gif", project.Preview);
    }

    [Theory]
    [InlineData("""{ "type": "scene" }""", WorkshopItemType.Scene)]
    [InlineData("""{ "type": "WEB" }""", WorkshopItemType.Web)]
    [InlineData("""{ "type": "Application" }""", WorkshopItemType.Application)]
    [InlineData("""{ "type": "preset" }""", WorkshopItemType.Unknown)]
    [InlineData("""{ "type": 3 }""", WorkshopItemType.Unknown)]
    [InlineData("""{ "title": ["x"], "type": "scene", "type": "video" }""", WorkshopItemType.Scene)]
    public void ProjectFileToleratesOddTypes(string json, WorkshopItemType expected)
    {
        var project = ProjectFile.Parse(json);

        Assert.NotNull(project);
        Assert.Equal(expected, project.Type);
        Assert.Null(project.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{ broken")]
    public void ProjectFileRejectsNonObjects(string json)
    {
        Assert.Null(ProjectFile.Parse(json));
    }

    [Fact]
    public void ScanDecidesStatusesAndSortsByTypeThenTitle()
    {
        using var dir = new TempDir();
        using var cache = new TempDir();
        var workshop = Path.Combine(dir.Path, "431960");
        Item(workshop, "100", """{ "title": "Big", "type": "video", "file": "big.mp4" }""", ("big.mp4", 101 * Mb));
        Item(workshop, "101", """{ "title": "Small", "type": "Video", "file": "small.mp4" }""", ("small.mp4", 5 * Mb));
        Item(workshop, "102", """{ "title": "Clip", "type": "video", "file": "clip.webm" }""", ("clip.webm", 1 * Mb));
        Item(workshop, "200", """{ "title": "b scene", "type": "Scene", "file": "scene.json" }""", ("scene.pkg", 2048));
        var converted = Item(workshop, "201", """{ "title": "A scene", "type": "scene", "file": "scene.json" }""", ("scene.pkg", 2048));
        Item(workshop, "300", """{ "title": "Page", "type": "web", "file": "index.html" }""", ("index.html", 100));
        Item(workshop, "400", """{ "title": "App", "type": "application", "file": "app.exe" }""", ("app.exe", 100));
        Item(workshop, "500", "{ not json", ("x.bin", 10));
        Directory.CreateDirectory(Path.Combine(workshop, "600")); // no project.json: skipped
        var cached = ConversionCache.PathFor(cache.Path, "201", ConversionCache.ModifiedUtc(converted));
        File.WriteAllBytes(cached, [1, 2, 3]);
        var log = new MemoryLog();

        var items = WorkshopLibrary.Scan([workshop], cache.Path, log);

        Assert.Equal(["100", "102", "101", "201", "200", "300", "400", "500"], items.Select(i => i.Id));
        Assert.Equal(Enumerable.Range(1, 8), items.Select(i => i.Number));
        var byId = items.ToDictionary(i => i.Id);
        Assert.Equal(WorkshopItemStatus.TooLarge, byId["100"].Status);
        Assert.Equal(101 * Mb, byId["100"].SizeBytes);
        Assert.Equal(WorkshopItemStatus.Ready, byId["101"].Status);
        Assert.Equal(Path.Combine(workshop, "101", "small.mp4"), byId["101"].PlayableFile);
        Assert.Equal(WorkshopItemStatus.Unsupported, byId["102"].Status);
        Assert.Equal(WorkshopItemStatus.Converted, byId["201"].Status);
        Assert.Equal(cached, byId["201"].PlayableFile);
        Assert.Equal(WorkshopItemStatus.NeedsConversion, byId["200"].Status);
        Assert.Null(byId["200"].PlayableFile);
        Assert.Equal(WorkshopItemStatus.NeedsConversion, byId["300"].Status);
        Assert.Equal(WorkshopItemStatus.Unsupported, byId["400"].Status);
        Assert.Equal(WorkshopItemStatus.Unsupported, byId["500"].Status);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void VideoPathCannotEscapeTheItemFolder()
    {
        using var dir = new TempDir();
        var folder = Item(dir.Path, "1", """{ "type": "video", "file": "..\\..\\secret.mp4" }""");

        var item = WorkshopLibrary.Describe("1", folder, ProjectFile.Read(folder)!, dir.Path, 100 * Mb);

        Assert.Equal(WorkshopItemStatus.Unsupported, item.Status);
        Assert.Null(item.FilePath);
        Assert.Equal("1", item.Title);
    }

    [Fact]
    public void SelectByNumberOrId()
    {
        var items = new[] { Fake(1, "2530355779"), Fake(2, "3"), Fake(3, "77") };

        Assert.Equal("2530355779", WorkshopLibrary.Select(items, "1")?.Id);
        Assert.Equal("3", WorkshopLibrary.Select(items, " 3 ")?.Id); // an id wins over a number
        Assert.Equal("2530355779", WorkshopLibrary.Select(items, "2530355779")?.Id);
        Assert.Equal("77", WorkshopLibrary.Select(items, "77")?.Id);
        Assert.Null(WorkshopLibrary.Select(items, "4"));
        Assert.Null(WorkshopLibrary.Select(items, "0"));
        Assert.Null(WorkshopLibrary.Select(items, "-1"));
        Assert.Null(WorkshopLibrary.Select(items, "abc"));
    }

    [Fact]
    public void TableAlignsColumnsAndPutsTitleLast()
    {
        var items = new[]
        {
            Fake(1, "2530355779", "Astronaut", 8_500_000, WorkshopItemStatus.NeedsConversion),
            Fake(10, "42", "Floating In Space", 218 * Mb, WorkshopItemStatus.TooLarge, WorkshopItemType.Video),
        };

        var lines = WorkshopLibrary.FormatTable(items).ToList();

        Assert.Equal(3, lines.Count);
        Assert.Equal(" #  Workshop id  Type       Size  Status            Title", lines[0]);
        Assert.Equal(" 1  2530355779   scene    8.1 MB  needs conversion  Astronaut", lines[1]);
        Assert.Equal("10  42           video  218.0 MB  too large         Floating In Space", lines[2]);
    }

    [Fact]
    public void CacheKeyChangesWithModifiedTimeAndFindsStaleCopies()
    {
        using var cache = new TempDir();
        var old = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var updated = old.AddDays(1);
        File.WriteAllBytes(ConversionCache.PathFor(cache.Path, "123", old), [1]);

        Assert.Equal("we-123-20250102T030405.mp4", ConversionCache.FileName("123", old));
        Assert.NotNull(ConversionCache.Find(cache.Path, "123", old));
        Assert.Null(ConversionCache.Find(cache.Path, "123", updated));
        Assert.Equal([ConversionCache.PathFor(cache.Path, "123", old)], ConversionCache.Stale(cache.Path, "123", updated));
        Assert.Empty(ConversionCache.Stale(cache.Path, "1234", updated));
        Assert.Equal("we-___x-20250102T030405.mp4", ConversionCache.FileName("../x", old));
    }

    [Fact]
    public void ParsesWeList()
    {
        var options = CommandLineParser.Parse(["--we-list"]);

        Assert.Empty(options.Errors);
        Assert.True(options.WeList);
        Assert.True(options.IsCommand);
        Assert.True(options.IsWallpaperEngineCommand);
        Assert.Contains("--we-list", CommandLineOptions.HelpText, StringComparison.Ordinal);
    }

    private static string Item(string workshop, string id, string projectJson, params (string Name, long Size)[] files)
    {
        var folder = Directory.CreateDirectory(Path.Combine(workshop, id)).FullName;
        File.WriteAllText(Path.Combine(folder, "project.json"), projectJson);
        foreach (var (name, size) in files)
        {
            using var stream = new FileStream(Path.Combine(folder, name), FileMode.Create);
            stream.SetLength(size);
        }

        return folder;
    }

    private static WorkshopItem Fake(
        int number,
        string id,
        string title = "t",
        long size = 1,
        WorkshopItemStatus status = WorkshopItemStatus.NeedsConversion,
        WorkshopItemType type = WorkshopItemType.Scene) =>
        new(number, id, @"C:\ws\" + id, title, type, null, size, DateTime.UnixEpoch, status, null, null);
}

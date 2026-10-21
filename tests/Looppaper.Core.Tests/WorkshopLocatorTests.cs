using Looppaper.Core.Configuration;
using Looppaper.Core.WallpaperEngine;

namespace Looppaper.Core.Tests;

public class WorkshopLocatorTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"apps"
        		{
        			"431960"		"826275581"
        		}
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        	}
        }
        """;

    [Fact]
    public void KeyValuesParsesNestedBlocksAndEscapes()
    {
        var root = KeyValues.Parse(LibraryFolders);

        var folders = root["libraryfolders"];
        Assert.NotNull(folders);
        Assert.Equal(2, folders.Children.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders["0"]?["path"]?.Value);
        Assert.Equal("826275581", folders["0"]?["apps"]?["431960"]?.Value);
        Assert.Equal(string.Empty, folders["0"]?["label"]?.Value);
    }

    [Fact]
    public void KeyValuesToleratesCommentsBareTokensConditionsAndBrokenInput()
    {
        const string text = """
            // comment
            root
            {
                bare value // trailing comment
                "cond" "x" [$WIN32]
                "unterminated" { "a" "1"
            """;

        var root = KeyValues.Parse(text);

        var node = root["root"];
        Assert.Equal("value", node?["bare"]?.Value);
        Assert.Equal("x", node?["cond"]?.Value);
        Assert.Equal("1", node?["unterminated"]?["a"]?.Value);
    }

    [Fact]
    public void KeyValuesKeepsUnescapedBackslashes()
    {
        var root = KeyValues.Parse("\"path\" \"D:\\Games\\Steam\"");

        Assert.Equal(@"D:\Games\Steam", root["path"]?.Value);
    }

    [Fact]
    public void LibraryPathsReadsCurrentAndOldLayouts()
    {
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], WorkshopLocator.LibraryPaths(LibraryFolders));

        const string old = """
            "LibraryFolders"
            {
                "TimeNextStatsReport" "123"
                "ContentStatsID" "-456"
                "1" "E:\\Steam"
            }
            """;
        Assert.Equal([@"E:\Steam"], WorkshopLocator.LibraryPaths(old));
        Assert.Empty(WorkshopLocator.LibraryPaths("garbage {"));
    }

    [Fact]
    public void LocatesWorkshopAndInstallThroughSteamLibraries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var steam = Path.Combine(dir.Path, "Steam");
        var library = Path.Combine(dir.Path, "Library2");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        var workshop = Directory.CreateDirectory(Path.Combine(library, "steamapps", "workshop", "content", "431960")).FullName;
        var install = Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "wallpaper_engine")).FullName;
        File.WriteAllText(Path.Combine(install, "wallpaper64.exe"), string.Empty);
        File.WriteAllText(
            Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{steam.Replace(@"\", @"\\")}\" }} \"1\" {{ \"path\" \"{library.Replace(@"\", @"\\")}\" }} }}");

        // Steam stores SteamPath with forward slashes and in lower case.
        var location = WorkshopLocator.Locate(new WallpaperEngineConfig(), steam.Replace('\\', '/').ToLowerInvariant(), new MemoryLog());

        Assert.Equal([workshop], location.WorkshopFolders, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(install, location.InstallFolder, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(install, "wallpaper64.exe"), location.WallpaperExe, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigOverridesWinAndMissingOnesAreWarned()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var workshop = Directory.CreateDirectory(Path.Combine(dir.Path, "ws")).FullName;
        var install = Directory.CreateDirectory(Path.Combine(dir.Path, "we")).FullName;
        File.WriteAllText(Path.Combine(install, "wallpaper64.exe"), string.Empty);
        var log = new MemoryLog();

        var found = WorkshopLocator.Locate(new WallpaperEngineConfig { WorkshopPath = workshop, InstallPath = install }, null, log);
        var missing = WorkshopLocator.Locate(new WallpaperEngineConfig { WorkshopPath = Path.Combine(dir.Path, "nope"), InstallPath = dir.Path }, null, log);

        Assert.Equal([workshop], found.WorkshopFolders);
        Assert.Equal(install, found.InstallFolder);
        Assert.Empty(missing.WorkshopFolders);
        Assert.Null(missing.InstallFolder);
        Assert.Equal(2, log.Warnings.Count());
    }

    [Fact]
    public void NoSteamMeansNothingFound()
    {
        var location = WorkshopLocator.Locate(new WallpaperEngineConfig(), null, new MemoryLog());

        Assert.Empty(location.WorkshopFolders);
        Assert.Null(location.WallpaperExe);
    }

    [Theory]
    [InlineData(@"D:\SteamLibrary\steamapps\workshop\content\431960", @"D:\SteamLibrary")]
    [InlineData(@"D:\Other\431960", null)]
    public void LibraryOfWorkshopFolder(string folder, string? expected)
    {
        Assert.Equal(expected, WorkshopLocator.LibraryOfWorkshopFolder(folder));
    }

    [Fact]
    public void ConfigReadsWallpaperEngineSection()
    {
        var log = new MemoryLog();
        var config = ConfigLoader.Parse("""{ "wallpaperEngine": { "workshopPath": "E:\\ws", "installPath": null, "extra": 1 } }""", log);

        Assert.Equal(@"E:\ws", config.WallpaperEngine.WorkshopPath);
        Assert.Null(config.WallpaperEngine.InstallPath);
        Assert.Single(log.Warnings);
        Assert.Contains("\"wallpaperEngine\"", ConfigLoader.Serialize(new LooppaperConfig()), StringComparison.Ordinal);
    }
}

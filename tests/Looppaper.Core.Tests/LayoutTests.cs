using Looppaper.Core.Configuration;

namespace Looppaper.Core.Tests;

/// <summary>These tests change the process-wide layout, so they run on their own.</summary>
[CollectionDefinition(nameof(LayoutCollection), DisableParallelization = true)]
public sealed class LayoutCollection
{
}

[Collection(nameof(LayoutCollection))]
public sealed class LayoutTests : IDisposable
{
    public void Dispose() => AppPaths.UsePortableRoot(null);

    [Fact]
    public void MarkerFileSelectsPortableLayoutAndEverythingLivesInTheExeFolder()
    {
        using var dir = new TempDir();
        dir.File(AppPaths.PortableMarker, 0);

        Assert.Equal(InstallLayout.Portable, AppPaths.Configure(dir.Path));
        Assert.Equal(Path.Combine(dir.Path, "config.json"), AppPaths.DefaultConfigFile);
        Assert.Equal(Path.Combine(dir.Path, "cache"), AppPaths.CacheDirectory);
        Assert.Equal(Path.Combine(dir.Path, "logs", "looppaper.log"), AppPaths.LogFile);
        Assert.Equal(Path.Combine(dir.Path, "compression.json"), AppPaths.CompressionStateFile);
        Assert.Equal("collection", new LooppaperConfig().CollectionFolder);
        Assert.Equal(Path.Combine(dir.Path, "collection"), new LooppaperConfig().ResolveCollectionFolder());
        Assert.Equal("collection", new LooppaperConfig().Video.Folder);
        Assert.Single(new LooppaperConfig().ResolveVideoFolders());
    }

    [Fact]
    public void WithoutMarkerTheStandardLayoutIsUsed()
    {
        using var dir = new TempDir();

        Assert.Equal(InstallLayout.Standard, AppPaths.Configure(dir.Path));
        Assert.Equal(AppPaths.Standard.ConfigFile, AppPaths.DefaultConfigFile);
        Assert.Equal(AppPaths.Standard.CacheDirectory, AppPaths.CacheDirectory);
        Assert.Equal(AppPaths.Standard.CollectionFolder, new LooppaperConfig().CollectionFolder);
        Assert.Equal(@"%USERPROFILE%\Videos\Looppaper", new LooppaperConfig().Video.Folder);
        Assert.Null(AppPaths.PortableRoot);
    }

    [Fact]
    public void AbsoluteCollectionFolderIsKeptInPortableLayout()
    {
        using var dir = new TempDir();
        AppPaths.UsePortableRoot(dir.Path);
        var elsewhere = Path.Combine(Path.GetTempPath(), "somewhere-else");

        Assert.Equal(elsewhere, new LooppaperConfig { CollectionFolder = elsewhere }.ResolveCollectionFolder());
    }
}

public sealed class PortableMigrationTests
{
    [Fact]
    public void CopiesConfigCacheCollectionAndRecordsWithoutTouchingTheOriginals()
    {
        using var oldDir = new TempDir();
        using var install = new TempDir();
        var oldCollection = Path.Combine(oldDir.Path, "Pictures", "Looppaper");
        var oldCache = Path.Combine(oldDir.Path, "Local", "cache");
        Directory.CreateDirectory(oldCollection);
        Directory.CreateDirectory(oldCache);
        File.WriteAllText(Path.Combine(oldCollection, "sunset.jpg"), "jpg");
        File.WriteAllText(Path.Combine(oldCollection, "notes.txt"), "not media");
        File.WriteAllText(Path.Combine(oldCache, "we-1-2.mp4"), "video");
        var cachedVideo = Path.Combine(oldCache, "we-1-2.mp4");
        var oldConfig = Path.Combine(oldDir.Path, "Roaming", "config.json");
        var config = new LooppaperConfig { CollectionFolder = oldCollection, IntervalSeconds = 60 };
        config.Video.File = cachedVideo;
        config.Video.Volume = 30;
        Assert.True(ConfigLoader.TrySave(oldConfig, config, new MemoryLog()));
        var oldState = Path.Combine(oldDir.Path, "Local", "compression.json");
        File.WriteAllText(oldState, System.Text.Json.JsonSerializer.Serialize(new { copy = cachedVideo }));

        var done = PortableMigration.Run(install.Path, new StandardLocations(oldConfig, oldCache, oldState, oldCollection), new MemoryLog());

        Assert.Equal(4, done.Count);
        var migrated = ConfigLoader.Parse(File.ReadAllText(Path.Combine(install.Path, "config.json")), new MemoryLog());
        Assert.Equal("collection", migrated.CollectionFolder);
        Assert.Equal(60, migrated.IntervalSeconds);
        Assert.Equal(30, migrated.Video.Volume);
        Assert.Equal(Path.Combine(install.Path, "cache", "we-1-2.mp4"), migrated.Video.File);
        Assert.True(File.Exists(Path.Combine(install.Path, "collection", "sunset.jpg")));
        Assert.False(File.Exists(Path.Combine(install.Path, "collection", "notes.txt")));
        Assert.True(File.Exists(Path.Combine(install.Path, "cache", "we-1-2.mp4")));
        Assert.Contains("we-1-2.mp4", File.ReadAllText(Path.Combine(install.Path, "compression.json")), StringComparison.Ordinal);
        Assert.Contains(Path.Combine(install.Path, "cache").Replace("\\", "\\\\", StringComparison.Ordinal), File.ReadAllText(Path.Combine(install.Path, "compression.json")), StringComparison.OrdinalIgnoreCase);

        // Originals untouched.
        Assert.True(File.Exists(oldConfig));
        Assert.True(File.Exists(Path.Combine(oldCollection, "sunset.jpg")));
        Assert.True(File.Exists(cachedVideo));
    }

    [Fact]
    public void CustomCollectionFolderIsKeptAsIs()
    {
        using var oldDir = new TempDir();
        using var install = new TempDir();
        var custom = Path.Combine(oldDir.Path, "MyWallpapers");
        var oldConfig = Path.Combine(oldDir.Path, "config.json");
        ConfigLoader.TrySave(oldConfig, new LooppaperConfig { CollectionFolder = custom }, new MemoryLog());

        PortableMigration.Run(install.Path, new StandardLocations(oldConfig, Path.Combine(oldDir.Path, "cache"), Path.Combine(oldDir.Path, "c.json"), Path.Combine(oldDir.Path, "Pictures")), new MemoryLog());

        var migrated = ConfigLoader.Parse(File.ReadAllText(Path.Combine(install.Path, "config.json")), new MemoryLog());
        Assert.Equal(custom, migrated.CollectionFolder);
        Assert.False(Directory.Exists(Path.Combine(install.Path, "collection")));
    }

    [Fact]
    public void DoesNothingWhenTheInstallAlreadyHasAConfigOrThereIsNoOldConfig()
    {
        using var oldDir = new TempDir();
        using var install = new TempDir();
        var locations = new StandardLocations(Path.Combine(oldDir.Path, "config.json"), oldDir.Path, Path.Combine(oldDir.Path, "c.json"), oldDir.Path);

        Assert.Empty(PortableMigration.Run(install.Path, locations, new MemoryLog()));

        ConfigLoader.TrySave(locations.ConfigFile, new LooppaperConfig(), new MemoryLog());
        File.WriteAllText(Path.Combine(install.Path, "config.json"), "{}");

        Assert.Empty(PortableMigration.Run(install.Path, locations, new MemoryLog()));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(install.Path, "config.json")));
    }

    [Fact]
    public void VideosOfTheDefaultVideoFolderComeIntoTheCollection()
    {
        using var oldDir = new TempDir();
        using var install = new TempDir();
        var oldVideos = Path.Combine(oldDir.Path, "Videos", "Looppaper");
        Directory.CreateDirectory(oldVideos);
        var clip = Path.Combine(oldVideos, "Rain (we-5-20250102T030405).mp4");
        File.WriteAllText(clip, "video");
        File.WriteAllText(Path.Combine(oldVideos, "notes.txt"), "not a video");
        var oldConfig = Path.Combine(oldDir.Path, "config.json");
        ConfigLoader.TrySave(oldConfig, new LooppaperConfig { CollectionFolder = Path.Combine(oldDir.Path, "Elsewhere"), Video = { File = clip } }, new MemoryLog());
        var locations = new StandardLocations(oldConfig, Path.Combine(oldDir.Path, "cache"), Path.Combine(oldDir.Path, "c.json"), Path.Combine(oldDir.Path, "Pictures"), oldVideos);

        var done = PortableMigration.Run(install.Path, locations, new MemoryLog());

        Assert.Contains(done, line => line.StartsWith("Copied 1 video(s)", StringComparison.Ordinal));
        var migrated = ConfigLoader.Parse(File.ReadAllText(Path.Combine(install.Path, "config.json")), new MemoryLog());
        Assert.Equal("collection", migrated.Video.Folder);
        Assert.Equal(Path.Combine(install.Path, "collection", "Rain (we-5-20250102T030405).mp4"), migrated.Video.File);
        Assert.True(File.Exists(migrated.Video.File));
        Assert.False(File.Exists(Path.Combine(install.Path, "collection", "notes.txt")));
        Assert.True(File.Exists(clip));
    }

    [Fact]
    public void RenameOnlyTouchesPathsInsideTheOldFolders()
    {
        var renames = new List<(string, string)> { (@"C:\old\cache", @"D:\app\cache") };

        Assert.Equal(@"D:\app\cache\a.mp4", PortableMigration.Rename(@"C:\old\cache\a.mp4", renames));
        Assert.Equal(@"C:\old\cache2\a.mp4", PortableMigration.Rename(@"C:\old\cache2\a.mp4", renames));
        Assert.Equal("a.mp4", PortableMigration.Rename("a.mp4", renames));
        Assert.Null(PortableMigration.Rename(null, renames));
    }
}

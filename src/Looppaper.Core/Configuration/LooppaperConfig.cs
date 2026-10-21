namespace Looppaper.Core.Configuration;

public enum WallpaperMode
{
    Slideshow,
    Single,
}

public enum ScalingMode
{
    /// <summary>Cover the monitor, cropping the overflow (keeps aspect ratio).</summary>
    Fill,

    /// <summary>Fit inside the monitor, letterboxing with the background color (keeps aspect ratio).</summary>
    Fit,

    /// <summary>Stretch to the monitor size, ignoring aspect ratio.</summary>
    Stretch,

    /// <summary>Original size, centered; larger pictures are cropped.</summary>
    Center,
}

/// <summary>Contents of <c>config.json</c>. Defaults here are what a first run writes.</summary>
public sealed class LooppaperConfig
{
    public const int DefaultIntervalSeconds = 300;
    public const int MinIntervalSeconds = 5;
    public const int MaxIntervalSeconds = 7 * 24 * 60 * 60;
    public const int DefaultMaxPictureSizeMb = 50;
    public const int MaxPictureSizeCapMb = 200;
    public const string DefaultBackgroundColor = "#000000";

    /// <summary>`collection` inside the install folder (portable layout) or Pictures\Looppaper (standard layout).</summary>
    public static string DefaultCollectionFolder =>
        AppPaths.Layout == InstallLayout.Portable ? AppPaths.PortableCollectionFolder : AppPaths.Standard.CollectionFolder;

    public string CollectionFolder { get; set; } = DefaultCollectionFolder;

    public WallpaperMode Mode { get; set; } = WallpaperMode.Slideshow;

    /// <summary>File name inside the collection folder, or a full path. Used in single mode.</summary>
    public string? Picture { get; set; }

    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;

    public bool Shuffle { get; set; }

    public ScalingMode Scaling { get; set; } = ScalingMode.Fill;

    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    public int MaxPictureSizeMb { get; set; } = DefaultMaxPictureSizeMb;

    /// <summary>
    /// Opt-in: when true the wallpaper asks GitHub once at start whether a newer release exists and logs it.
    /// Nothing is downloaded or installed.
    /// </summary>
    public bool CheckForUpdates { get; set; }

    public VideoConfig Video { get; set; } = new();

    /// <summary>When to pause the video and the slideshow because nobody can see them (Epic 2).</summary>
    public PauseConfig Pause { get; set; } = new();

    /// <summary>ffmpeg.exe or its folder; null means search PATH, winget, scoop, Chocolatey and common folders.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>Where to find Wallpaper Engine's Workshop downloads and install (Epic 8); null fields are searched for.</summary>
    public WallpaperEngineConfig WallpaperEngine { get; set; } = new();

    /// <summary>Absolute collection folder with environment variables expanded; a relative path is inside the install folder (portable layout).</summary>
    public string ResolveCollectionFolder() => AppPaths.ResolveDataPath(CollectionFolder);

    /// <summary>Absolute path of the configured single picture, or null when none is set.</summary>
    public string? ResolvePicture() => ResolveInCollection(Picture);

    /// <summary>Absolute path of the configured video file, or null to use the first video in the collection.</summary>
    public string? ResolveVideo() => ResolveInCollection(Video.File);

    /// <summary>Absolute path of a file name in the collection folder, or of a full path; null when empty.</summary>
    public string? ResolveInCollection(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(file.Trim().Trim('"'));
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(ResolveCollectionFolder(), expanded));
    }

    public RgbColor ResolveBackgroundColor() =>
        RgbColor.TryParse(BackgroundColor, out var color) ? color : RgbColor.Black;
}

public sealed class VideoConfig
{

    /// <summary>When true and a video is available, the looping video is the wallpaper instead of pictures.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>File name in the collection folder, or a full path; null means the first .mp4 in the collection.</summary>
    public string? File { get; set; }

    /// <summary>0..100; 0 mutes the video.</summary>
    public int Volume { get; set; }

    /// <summary>
    /// Compress every video once (into the cache) before it becomes the wallpaper: scaled to the screen and
    /// re-encoded at a wallpaper-friendly bitrate. The original is kept. False plays videos as they are.
    /// </summary>
    public bool Compress { get; set; } = true;
}

/// <summary>The <c>pause</c> section: when playback pauses. Paused video keeps its last frame; the slideshow holds.</summary>
public sealed class PauseConfig
{
    /// <summary>Pause while a full-screen app or game covers a monitor (only that monitor, unless it is an exclusive full-screen game).</summary>
    public bool OnFullscreen { get; set; } = true;
}

/// <summary>The <c>wallpaperEngine</c> section: overrides for the folders found through Steam.</summary>
public sealed class WallpaperEngineConfig
{
    /// <summary>The Workshop content folder (<c>...\steamapps\workshop\content\431960</c>); null = find it through Steam.</summary>
    public string? WorkshopPath { get; set; }

    /// <summary>The Wallpaper Engine folder with <c>wallpaper64.exe</c>; null = find it through Steam.</summary>
    public string? InstallPath { get; set; }
}

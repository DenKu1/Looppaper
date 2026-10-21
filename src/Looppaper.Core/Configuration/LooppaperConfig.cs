using Looppaper.Core.Playback;

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

    /// <summary>`collection` inside the install folder (portable layout) or Videos\Looppaper (standard layout).</summary>
    public static string DefaultVideoFolder =>
        AppPaths.Layout == InstallLayout.Portable ? AppPaths.PortableCollectionFolder : AppPaths.Standard.VideoFolder;

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

    /// <summary>
    /// Run a small watchdog process next to the wallpaper: if the wallpaper ends without a clean stop, it puts the
    /// Windows wallpaper back, and after a crash it starts Looppaper again (at most 3 times in 10 minutes).
    /// </summary>
    public bool Watchdog { get; set; } = true;

    /// <summary>Show a notification-area icon with a small menu (next / previous wallpaper, pause, open folders, stop). On by default; the installer asks.</summary>
    public bool TrayIcon { get; set; } = true;

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

    /// <summary>Absolute video folder (<c>video.folder</c>), resolved like <see cref="ResolveCollectionFolder"/>.</summary>
    public string ResolveVideoFolder() => AppPaths.ResolveDataPath(Video.Folder);

    /// <summary>Where videos are found: the video folder, then the collection folder (once when they are the same).</summary>
    public IReadOnlyList<string> ResolveVideoFolders()
    {
        var videos = ResolveVideoFolder();
        var collection = ResolveCollectionFolder();
        return string.Equals(Path.TrimEndingDirectorySeparator(videos), Path.TrimEndingDirectorySeparator(collection), StringComparison.OrdinalIgnoreCase)
            ? [videos]
            : [videos, collection];
    }

    /// <summary>
    /// Absolute path of the configured video file, or null when none is set. A file name (or relative path) is
    /// looked up in the video folder first, then in the collection folder.
    /// </summary>
    public string? ResolveVideo()
    {
        if (string.IsNullOrWhiteSpace(Video.File))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(Video.File.Trim().Trim('"'));
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var inVideoFolder = Path.GetFullPath(Path.Combine(ResolveVideoFolder(), expanded));
        return File.Exists(inVideoFolder) ? inVideoFolder : Path.GetFullPath(Path.Combine(ResolveCollectionFolder(), expanded));
    }

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
    /// <summary>When true and a video is available, looping videos are the wallpaper instead of pictures.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The video to start with: a file name in the video or collection folder, or a full path; null starts with
    /// the first video found.
    /// </summary>
    public string? File { get; set; }

    /// <summary>
    /// Folder for videos (<see cref="LooppaperConfig.DefaultVideoFolder"/>); Wallpaper Engine recordings are saved
    /// here. Videos in the collection folder play too.
    /// </summary>
    public string Folder { get; set; } = LooppaperConfig.DefaultVideoFolder;

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

    /// <summary>Pause while the session is locked or disconnected.</summary>
    public bool OnLock { get; set; } = true;

    /// <summary>Pause while battery saver / Energy Saver is on.</summary>
    public bool OnBatterySaver { get; set; } = true;

    /// <summary>Pause whenever the PC runs on battery.</summary>
    public bool OnBattery { get; set; }

    /// <summary>Whether this reason pauses playback. Sleep and the display turning off always do.</summary>
    public bool Allows(PauseReason reason) => reason switch
    {
        PauseReason.ExclusiveFullscreen => OnFullscreen,
        PauseReason.Locked or PauseReason.Disconnected => OnLock,
        PauseReason.BatterySaver => OnBatterySaver,
        PauseReason.OnBattery => OnBattery,
        _ => true,
    };
}

/// <summary>The <c>wallpaperEngine</c> section: overrides for the folders found through Steam.</summary>
public sealed class WallpaperEngineConfig
{
    /// <summary>The Workshop content folder (<c>...\steamapps\workshop\content\431960</c>); null = find it through Steam.</summary>
    public string? WorkshopPath { get; set; }

    /// <summary>The Wallpaper Engine folder with <c>wallpaper64.exe</c>; null = find it through Steam.</summary>
    public string? InstallPath { get; set; }
}

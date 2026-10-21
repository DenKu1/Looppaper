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
    public const string DefaultCollectionFolder = @"%USERPROFILE%\Pictures\Looppaper";
    public const int DefaultIntervalSeconds = 300;
    public const int MinIntervalSeconds = 5;
    public const int MaxIntervalSeconds = 7 * 24 * 60 * 60;
    public const int DefaultMaxPictureSizeMb = 50;
    public const int MaxPictureSizeCapMb = 200;
    public const string DefaultBackgroundColor = "#000000";

    public string CollectionFolder { get; set; } = DefaultCollectionFolder;

    public WallpaperMode Mode { get; set; } = WallpaperMode.Slideshow;

    /// <summary>File name inside the collection folder, or a full path. Used in single mode.</summary>
    public string? Picture { get; set; }

    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;

    public bool Shuffle { get; set; }

    public ScalingMode Scaling { get; set; } = ScalingMode.Fill;

    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    public int MaxPictureSizeMb { get; set; } = DefaultMaxPictureSizeMb;

    public VideoConfig Video { get; set; } = new();

    /// <summary>ffmpeg.exe or its folder; null means search PATH, winget, scoop, Chocolatey and common folders.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>Absolute collection folder with environment variables expanded.</summary>
    public string ResolveCollectionFolder() => AppPaths.ExpandPath(CollectionFolder);

    /// <summary>Absolute path of the configured single picture, or null when none is set.</summary>
    public string? ResolvePicture() => ResolveInCollection(Picture);

    /// <summary>Absolute path of the configured video file, or null to use the first video in the collection.</summary>
    public string? ResolveVideo() => ResolveInCollection(Video.File);

    private string? ResolveInCollection(string? file)
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
    /// <summary>Videos are limited to 100 MB; larger values in config.json are clamped down to this.</summary>
    public const int DefaultMaxFileSizeMb = 100;
    public const int MaxFileSizeCapMb = 100;

    /// <summary>When true and a video is available, the looping video is the wallpaper instead of pictures.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>File name in the collection folder, or a full path; null means the first .mp4 in the collection.</summary>
    public string? File { get; set; }

    /// <summary>0..100; 0 mutes the video.</summary>
    public int Volume { get; set; }

    public int MaxFileSizeMb { get; set; } = DefaultMaxFileSizeMb;
}
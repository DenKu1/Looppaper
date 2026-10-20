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

    public MusicConfig Music { get; set; } = new();

    /// <summary>Absolute collection folder with environment variables expanded.</summary>
    public string ResolveCollectionFolder() => AppPaths.ExpandPath(CollectionFolder);

    /// <summary>Absolute music folder; falls back to the collection folder.</summary>
    public string ResolveMusicFolder() =>
        string.IsNullOrWhiteSpace(Music.Folder) ? ResolveCollectionFolder() : AppPaths.ExpandPath(Music.Folder);

    /// <summary>Absolute path of the configured single picture, or null when none is set.</summary>
    public string? ResolvePicture()
    {
        if (string.IsNullOrWhiteSpace(Picture))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(Picture.Trim().Trim('"'));
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(ResolveCollectionFolder(), expanded));
    }

    public RgbColor ResolveBackgroundColor() =>
        RgbColor.TryParse(BackgroundColor, out var color) ? color : RgbColor.Black;
}

public sealed class MusicConfig
{
    public const int DefaultVolume = 40;
    public const int DefaultMaxFileSizeMb = 20;
    public const int MaxFileSizeCapMb = 100;

    public bool Enabled { get; set; } = true;

    /// <summary>Folder with MP3 files; null means the collection folder.</summary>
    public string? Folder { get; set; }

    public bool Shuffle { get; set; }

    /// <summary>0..100.</summary>
    public int Volume { get; set; } = DefaultVolume;

    public int MaxFileSizeMb { get; set; } = DefaultMaxFileSizeMb;
}

using Looppaper.Core.Logging;

namespace Looppaper.Core.Configuration;

/// <summary>Clamps out-of-range values in place, logging a warning for each correction.</summary>
public static class ConfigValidator
{
    public static void Normalize(LooppaperConfig config, ILog log)
    {
        if (string.IsNullOrWhiteSpace(config.CollectionFolder))
        {
            log.Warn($"Config: 'collectionFolder' is empty; using {LooppaperConfig.DefaultCollectionFolder}");
            config.CollectionFolder = LooppaperConfig.DefaultCollectionFolder;
        }

        config.IntervalSeconds = Clamp(
            config.IntervalSeconds, LooppaperConfig.MinIntervalSeconds, LooppaperConfig.MaxIntervalSeconds, "intervalSeconds", log);
        config.MaxPictureSizeMb = Clamp(config.MaxPictureSizeMb, 1, LooppaperConfig.MaxPictureSizeCapMb, "maxPictureSizeMb", log);

        if (!RgbColor.TryParse(config.BackgroundColor, out var color))
        {
            log.Warn($"Config: 'backgroundColor' value '{config.BackgroundColor}' is not #RRGGBB; using {LooppaperConfig.DefaultBackgroundColor}");
            config.BackgroundColor = LooppaperConfig.DefaultBackgroundColor;
        }
        else
        {
            config.BackgroundColor = color.ToString();
        }

        config.Video ??= new VideoConfig();
        config.Video.Volume = Clamp(config.Video.Volume, 0, 100, "video.volume", log);
        config.Video.MaxFileSizeMb = Clamp(config.Video.MaxFileSizeMb, 1, VideoConfig.MaxFileSizeCapMb, "video.maxFileSizeMb", log);
    }

    private static int Clamp(int value, int min, int max, string name, ILog log)
    {
        var clamped = Math.Clamp(value, min, max);
        if (clamped != value)
        {
            log.Warn($"Config: '{name}' value {value} is outside {min}..{max}; using {clamped}");
        }

        return clamped;
    }
}

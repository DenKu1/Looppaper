using System.Globalization;
using Looppaper.Core.Configuration;

namespace Looppaper.Core.CommandLine;

/// <summary>Parsed command line. Overrides are applied in memory only; config.json is not rewritten.</summary>
public sealed class CommandLineOptions
{
    public const string HelpText =
        """
        Looppaper - live wallpaper for Windows (no UI).

        Usage: Looppaper.exe [options]

          --config <path>        Use this config file instead of %APPDATA%\Looppaper\config.json
          --folder <path>        Collection folder with pictures and videos
          --picture <file>       Show one picture (file name in the collection or full path); implies --mode single and --no-video
          --interval <seconds>   Slideshow interval (5..604800)
          --mode <mode>          slideshow | single
          --shuffle              Shuffle the slideshow
          --video <file>         Play this .mp4 looped as the wallpaper (file name in the collection or full path)
          --no-video             Show pictures even if the collection has a video
          --volume <0-100>       Video volume (0 = muted)
          --stop                 Stop the running instance and restore the wallpaper
          --install-startup      Start Looppaper when you sign in (current user)
          --uninstall-startup    Remove Looppaper from sign-in startup
          --help                 Show this help

        Options accept "--name value" or "--name=value". Logs: %LOCALAPPDATA%\Looppaper\logs\looppaper.log
        """;

    public string? ConfigPath { get; set; }

    public string? Folder { get; set; }

    public string? Picture { get; set; }

    public int? IntervalSeconds { get; set; }

    public WallpaperMode? Mode { get; set; }

    public bool Shuffle { get; set; }

    public string? Video { get; set; }

    public bool NoVideo { get; set; }

    public int? Volume { get; set; }

    public bool Stop { get; set; }

    public bool InstallStartup { get; set; }

    public bool UninstallStartup { get; set; }

    public bool Help { get; set; }

    public List<string> Errors { get; } = [];

    /// <summary>True when the invocation performs a command and exits instead of running the wallpaper.</summary>
    public bool IsCommand => Help || Stop || InstallStartup || UninstallStartup;

    public void ApplyTo(LooppaperConfig config)
    {
        if (Folder is not null)
        {
            config.CollectionFolder = Folder;
        }

        if (Mode is { } mode)
        {
            config.Mode = mode;
        }

        if (Video is not null)
        {
            config.Video.File = Video;
            config.Video.Enabled = true;
        }

        if (Picture is not null)
        {
            config.Picture = Picture;
            config.Mode = WallpaperMode.Single;
            config.Video.Enabled = false;
        }

        if (IntervalSeconds is { } interval)
        {
            config.IntervalSeconds = interval;
        }

        if (Shuffle)
        {
            config.Shuffle = true;
        }

        if (NoVideo)
        {
            config.Video.Enabled = false;
        }

        if (Volume is { } volume)
        {
            config.Video.Volume = volume;
        }
    }

    /// <summary>Human-readable summary of the overrides, for the log.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        void Add(string name, object? value)
        {
            if (value is not null)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{name}={value}"));
            }
        }

        Add("config", ConfigPath);
        Add("folder", Folder);
        Add("picture", Picture);
        Add("interval", IntervalSeconds);
        Add("mode", Mode);
        Add("shuffle", Shuffle ? true : null);
        Add("video", Video);
        Add("noVideo", NoVideo ? true : null);
        Add("volume", Volume);
        return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
    }
}

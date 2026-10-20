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
          --folder <path>        Collection folder with pictures (and music)
          --picture <file>       Show one picture (file name in the collection or full path); implies --mode single
          --interval <seconds>   Slideshow interval (5..604800)
          --mode <mode>          slideshow | single
          --shuffle              Shuffle the slideshow
          --no-music             Do not play music
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

    public bool NoMusic { get; set; }

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

        if (Picture is not null)
        {
            config.Picture = Picture;
            config.Mode = WallpaperMode.Single;
        }

        if (IntervalSeconds is { } interval)
        {
            config.IntervalSeconds = interval;
        }

        if (Shuffle)
        {
            config.Shuffle = true;
        }

        if (NoMusic)
        {
            config.Music.Enabled = false;
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
        Add("noMusic", NoMusic ? true : null);
        return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
    }
}

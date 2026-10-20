# Looppaper

A lightweight live-wallpaper engine for Windows 10/11 with no UI. Looppaper draws pictures from a
folder behind your desktop icons — one fixed picture or a slideshow — and can play a folder of MP3s
in the background. Everything is configured with a JSON file and command-line switches.

> Status: work in progress. See [docs/EPICS.md](docs/EPICS.md) for the roadmap.

## Configuration

On first run Looppaper writes `%APPDATA%\Looppaper\config.json` with the defaults below. The file
may contain `//` and `/* */` comments and trailing commas. Invalid values are clamped (with a warning
in the log); a file that is not valid JSON is ignored and the defaults are used.

```jsonc
{
  "collectionFolder": "%USERPROFILE%\\Pictures\\Looppaper", // pictures (and, by default, music)
  "mode": "slideshow",          // "slideshow" or "single"
  "picture": null,              // single mode: file name in the collection, or a full path
  "intervalSeconds": 300,       // slideshow interval, 5..604800
  "shuffle": false,             // random slideshow order
  "scaling": "fill",            // "fill" | "fit" | "stretch" | "center"
  "backgroundColor": "#000000", // shown around "fit"/"center" pictures and when there is nothing to show
  "maxPictureSizeMb": 50,       // larger pictures are skipped (cap 200)
  "music": {
    "enabled": true,
    "folder": null,             // null = the collection folder
    "shuffle": false,
    "volume": 40,               // 0..100
    "maxFileSizeMb": 20         // larger MP3s are skipped (cap 100)
  }
}
```

Supported files: `.jpg`, `.jpeg`, `.png` pictures and `.mp3` music, in the top level of the folder.

## Command line

```
Looppaper.exe [options]

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
```

Command-line options override the config file for that run only; the file is not changed.

## Logs

`%LOCALAPPDATA%\Looppaper\logs\looppaper.log` (rolls over at 1 MB, one previous file kept as `looppaper.log.1`).

## Build

Requires the .NET 8 SDK (or newer).

```powershell
dotnet build
dotnet test
```

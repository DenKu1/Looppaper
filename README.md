# Looppaper

A lightweight live-wallpaper engine for Windows 10/11 with no UI. Looppaper draws pictures from a
folder behind your desktop icons — one fixed picture or a slideshow — and can play a folder of MP3s
in the background. Everything is configured with a JSON file and command-line switches.

> Status: Epic 1 (core engine) is done. See [docs/EPICS.md](docs/EPICS.md) for the roadmap.

## Install

No admin rights and no .NET install needed.

1. Download `Looppaper-win-x64.zip` from the latest [build artifact](https://github.com/DenKu1/Looppaper/actions/workflows/build.yml)
   (or build it yourself, see below) and extract it.
2. In the extracted folder run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

   This copies `Looppaper.exe` to `%LOCALAPPDATA%\Programs\Looppaper`, creates
   `%USERPROFILE%\Pictures\Looppaper`, registers Looppaper to start when you sign in, and starts it.
3. Put `.jpg` / `.jpeg` / `.png` pictures (and optionally `.mp3` music) into `Pictures\Looppaper`.
   Looppaper rescans the folder at every slideshow step; after editing `config.json`, restart it
   (`Looppaper.exe --stop`, then start it again).

To remove it: `powershell -ExecutionPolicy Bypass -File .\uninstall.ps1` (add `-RemoveSettings` to
also delete the config and logs). Your pictures and music are never deleted.

## How it works

Looppaper creates one borderless child window inside Explorer's desktop window, between the
desktop icons and Windows' own wallpaper (the same technique Lively Wallpaper uses; both the
Windows 11 24H2+ layout and the older WorkerW layout are supported). Each picture is decoded once,
scaled for every monitor into a single cached bitmap, and painted with one `BitBlt`; nothing runs
between slideshow steps. Music plays through the MCI player built into Windows. When Looppaper
stops, the window is removed and your normal wallpaper is redrawn.

## Resource budget

Targets: **~0% CPU while idle** (nothing runs between slideshow steps) and **under 60 MB of RAM**.
Looppaper runs at *below normal* priority, opts into Windows EcoQoS (efficiency mode), and after
each picture change compacts its heap and trims its working set.

Measured with the published single-file exe on Windows 11 25H2 (build 26200), AMD Ryzen 7 7700,
one 2560x1440 monitor, sampled every second for 60 s:

| Scenario | CPU (one core) | Working set | Private bytes |
|---|---|---|---|
| Single picture, no music (idle) | 0.000% (0 ms in 60 s) | 24 MB | 38 MB |
| Single picture + MP3 playback | 0.85% | 34–36 MB | 41 MB |
| Slideshow every **5 s** + MP3 playback (stress) | 2.1% | 16–47 MB (peak while scaling) | 41–52 MB |

With the default 5-minute interval the slideshow cost is one ~50 ms render every 300 s. Single-file
compression keeps the exe at ~34 MB instead of ~65 MB but adds ~15 MB of private memory (the runtime
is unpacked into RAM); idle private bytes drop to ~23 MB with compression turned off
(`-p:EnableCompressionInSingleFile=false`).

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
dotnet publish src/Looppaper -p:PublishProfile=win-x64   # -> publish\Looppaper.exe (self-contained, single file)
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

CI (`.github/workflows/build.yml`) builds, tests and uploads `Looppaper-win-x64.zip` (exe + scripts + README) on every push.

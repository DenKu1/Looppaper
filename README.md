# Looppaper

A lightweight live-wallpaper engine for Windows 10/11 with no UI. Looppaper puts a looping `.mp4`
video, one fixed picture, or a slideshow of pictures from a folder behind your desktop icons.
Everything is configured with a JSON file and command-line switches.

> Status: Epic 1 (core engine) is done. See [docs/EPICS.md](docs/EPICS.md) for the roadmap.

## Install

No admin rights and no .NET install needed.

1. Download `Looppaper-X.Y.Z-win-x64.zip` from the latest [release](https://github.com/DenKu1/Looppaper/releases)
   (or `Looppaper-win-x64.zip` from a [build artifact](https://github.com/DenKu1/Looppaper/actions/workflows/build.yml),
   or build it yourself, see below) and extract it.
2. In the extracted folder run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

   This installs Looppaper to `%LOCALAPPDATA%\Programs\Looppaper`, asks where it should keep its files
   (the usual Windows folders, or everything in the install folder; see [Folder layout](#folder-layout)),
   registers it to start when you sign in, and starts it. Add `-Layout Standard` or `-Layout Portable`
   to skip the question.
3. Put an `.mp4` video, or `.jpg` / `.jpeg` / `.png` pictures, into the collection folder:
   `Pictures\Looppaper` (standard) or `collection` inside the install folder (everything in one folder).
   If the folder has a video (and video is enabled) it plays looped, muted by default; otherwise the
   pictures are shown. Looppaper rescans the folder at every slideshow step; after adding a video or
   editing `config.json`, restart it (`Looppaper.exe --stop`, then start it again).

To remove it: `powershell -ExecutionPolicy Bypass -File .\uninstall.ps1`. It removes the program,
cache and logs; add `-RemoveSettings` to also delete `config.json`, and `-RemoveCollection` to delete
the `collection` folder. Without those, your settings and your pictures and videos are kept.

### Folder layout

The installer asks which one you want.

**Standard (default).** The usual Windows folders: `config.json` in `%APPDATA%\Looppaper`; `cache\`,
`logs\` and `compression.json` in `%LOCALAPPDATA%\Looppaper`; your pictures and videos in
`%USERPROFILE%\Pictures\Looppaper`. Any `Looppaper.exe` without the marker file below (for example a
`publish\` build) uses this layout.

**Everything in one folder** (`install.ps1 -Layout Portable`). Everything lives next to `Looppaper.exe`:

```
%LOCALAPPDATA%\Programs\Looppaper\
  Looppaper.exe
  looppaper.portable      (empty marker file: "keep everything in this folder")
  config.json             settings
  collection\             your pictures and videos
  cache\                  converted / compressed videos
  compression.json        which videos already have a compressed copy
  logs\looppaper.log
```

The marker file is what switches this on, so any folder with `Looppaper.exe` and an empty
`looppaper.portable` works the same way (`install.ps1 -Layout Portable -InstallDir D:\Apps\Looppaper`
installs there). On its first start in such a folder, Looppaper copies the settings, the cached videos
and the pictures/videos of `Pictures\Looppaper` from the standard folders (nothing is moved or deleted).
## How it works

Looppaper creates one borderless child window inside Explorer's desktop window, between the
desktop icons and Windows' own wallpaper (the same technique Lively Wallpaper uses; both the
Windows 11 24H2+ layout and the older WorkerW layout are supported). Each picture is decoded once,
scaled for every monitor into a single cached bitmap, and painted with one `BitBlt`; nothing runs
between slideshow steps. Videos play through the Media Foundation Media Engine built into Windows:
the GPU decodes them and presents frames straight into a child window, so Looppaper does no
per-frame work itself. Each monitor gets its own video window (only the first plays sound). If a
video cannot be played, Looppaper logs why and falls back to the pictures. When Looppaper stops,
the window is removed and your normal wallpaper is redrawn.

## Resource budget

Pictures: **~0% CPU while idle** (nothing runs between slideshow steps) and **under 60 MB of RAM**.
Video: **a few percent of one core**, with decoding on the GPU's video engine; the Direct3D and Media
Foundation stack needs **~110–140 MB of RAM**. Looppaper runs at *below normal* priority, opts into
Windows EcoQoS (efficiency mode), and after each picture change compacts its heap and trims its
working set.

Measured with the published single-file exe on Windows 11 25H2 (build 26200), AMD Ryzen 7 7700,
one 2560x1440 monitor:

| Scenario | CPU (one core) | GPU | Working set | Private bytes |
|---|---|---|---|---|
| Single picture (idle, 30 s) | 0.000% (0 ms) | — | 22 MB | 38 MB |
| Slideshow every **5 s**, 1920x1080 pictures (stress, 60 s) | 1.1% | — | 15–54 MB (peak while scaling) | 38–68 MB |
| Looping 1178x670 H.264 video, muted, fill (48 s) | 1.9% | video decode 2.7%, 3D 0.8% | 114–121 MB | 141 MB |

With the default 5-minute interval the slideshow cost is one ~50 ms render every 300 s. Videos above
the GPU's hardware decoder limit (e.g. H.264 larger than 4096x2304) fall back to software decoding,
which costs far more CPU and RAM (a 4500x3000 test video used ~150% of a core and 1.5 GB), so keep
videos at or below your monitor resolution. Single-file compression keeps the exe at ~34 MB instead
of ~65 MB but adds ~15 MB of private memory; build with `-p:EnableCompressionInSingleFile=false` to
trade disk for RAM.

## Configuration

On first run Looppaper writes `config.json` (in `%APPDATA%\Looppaper`, or in the install folder when
everything is kept in one folder) with the defaults below. Relative paths in it are relative to the install folder. The file
may contain `//` and `/* */` comments and trailing commas. Invalid values are clamped (with a warning
in the log); a file that is not valid JSON is ignored and the defaults are used.

```jsonc
{
  "collectionFolder": "%USERPROFILE%\\Pictures\\Looppaper", // pictures and videos; "collection" when everything is in one folder
  "mode": "slideshow",          // "slideshow" or "single"
  "picture": null,              // single mode: file name in the collection, or a full path
  "intervalSeconds": 300,       // slideshow interval, 5..604800
  "shuffle": false,             // random slideshow order
  "scaling": "fill",            // "fill" | "fit" | "stretch" | "center"
  "backgroundColor": "#000000", // shown around "fit"/"center" pictures and when there is nothing to show
  "maxPictureSizeMb": 50,       // larger pictures are skipped (cap 200)
  "checkForUpdates": false,     // true = check GitHub once at start for a newer release and log it (never downloads)
  "ffmpegPath": null,          // ffmpeg.exe or its folder; null = search PATH, winget, scoop, Chocolatey (only needed for conversion and compression)
  "video": {
    "enabled": true,            // false = always show pictures
    "file": null,               // file name in the collection, or a full path; null = first .mp4 in the collection
    "volume": 0,                // 0..100; 0 = muted
    "compress": true            // compress each video once (into the cache) before playing it; see "Video compression"
  },
  "wallpaperEngine": {
    "workshopPath": null,       // Wallpaper Engine Workshop folder (...\\steamapps\\workshop\\content\\431960); null = found through Steam
    "installPath": null         // folder with wallpaper64.exe; null = found through Steam
  }
}
```

Supported files: `.jpg`, `.jpeg`, `.png` pictures and `.mp4` videos (H.264 / HEVC, as supported by
Windows' own video player), in the top level of the folder. When video is enabled and a video is
available it is the wallpaper; `mode`, `picture`, `intervalSeconds` and `shuffle` apply to pictures.
`scaling` and `backgroundColor` apply to both.

## Command line

```
Looppaper.exe [options]

  --config <path>        Use this config file instead of the default config.json
  --folder <path>        Collection folder with pictures and videos
  --picture <file>       Show one picture (file name in the collection or full path); implies --mode single and --no-video
  --interval <seconds>   Slideshow interval (5..604800)
  --mode <mode>          slideshow | single
  --shuffle              Shuffle the slideshow
  --video <file>         Play this .mp4 looped as the wallpaper (file name in the collection or full path)
  --no-video             Show pictures even if the collection has a video
  --volume <0-100>       Video volume (0 = muted)
  --compress <file>      Compress a video for the wallpaper now, with progress (needs ffmpeg)
  --stop                 Stop the running instance and restore the wallpaper
  --install-startup      Start Looppaper when you sign in (current user)
  --uninstall-startup    Remove Looppaper from sign-in startup
  --we-list              List the Wallpaper Engine Workshop wallpapers downloaded on this PC
  --we-set <number|id>   Use that Wallpaper Engine wallpaper (number from --we-list, or Workshop id)
  --we-pick              Show the list and ask which Wallpaper Engine wallpaper to use
  --we-convert <num|id>  Record a Wallpaper Engine scene / web wallpaper to a looping .mp4 (cached)
  --no-restart           With --we-set / --we-pick: only update config.json, don't restart Looppaper
  --check-update         Check GitHub once for a newer release and print the result (never downloads)
  --help                 Show this help
```

Command-line options override the config file for that run only; the file is not changed.

Commands such as `--help` and `--stop` print their result in the terminal you run them from. Starting the wallpaper from a terminal hands it to a background copy and returns straight away, so you can close the terminal. Started from Explorer or at sign-in no console window appears (Windows 11 24H2 and later; older Windows may flash a console window for a moment).

## Video compression

Every video is compressed once before it becomes the wallpaper (`video.compress`, on by default), so it
uses less memory, disk and GPU. Looppaper **tells you** rather than asks, and never changes or deletes the
original. Nothing is refused because of its size.

- **At start-up (no terminal):** the video starts playing straight away as it is, a Windows notification
  says it is being compressed in the background, and the wallpaper switches to the lighter copy when it is
  ready. Next time the copy plays immediately.
- **From a terminal:** `Looppaper.exe --compress <file>` (a file name in the collection or a full path), and
  `--we-set` / `--we-pick` on a Wallpaper Engine video, compress right there with a progress line
  (Ctrl+C cancels):

  ```
  Compressing Floating_In_Space.mp4 (217.9 MB) for the wallpaper: about 92.1 MB (1920x1080, 6.2 Mbit/s video + 128 kbit/s audio, 1:52). The original is kept; ...
  Encoder: NVIDIA NVENC
    100% (00:01:52, 9.3x)
  Compressed 217.9 MB to 88.4 MB: ...\cache\Floating_In_Space-1a2b3c4d5e.mp4
  ```

- **Still over 100 MB?** Long or very detailed videos can stay large. They still play, but Looppaper warns
  (in the terminal, or with a notification) that a video over 100 MB can slow your PC down.
- **Already compact?** If compressing would not make the video smaller (and it is no larger than your
  screen), the original is kept as it is and remembered, so it is not tried again.
- **No ffmpeg?** The original plays uncompressed, and you are told once how to install ffmpeg.

How it compresses: a video larger than your biggest monitor is scaled down to still cover it (never up,
aspect ratio kept); the bitrate is about 0.1 bit per pixel per frame (about 6 Mbit/s at 1080p30, 11 Mbit/s
at 1440p30), never more than the original's, plus 128 kbit/s audio when it has sound; and the first working
encoder is used: NVIDIA NVENC, AMD AMF, Intel Quick Sync, else x264 on the CPU. The copy is checked (readable
by ffprobe) and saved as `cache\<name>-<hash>.mp4`; from then on Looppaper plays it whenever the original is
chosen. Copies are recorded in `compression.json`. Deleting the cache folder is safe (videos are simply
compressed again). Set `"video": { "compress": false }` to play videos exactly as they are.
ffmpeg is not included with Looppaper. Install it with `winget install Gyan.FFmpeg`, or set `ffmpegPath` in
`config.json` to `ffmpeg.exe` or its folder; `ffprobe.exe` must be next to it. Looppaper also finds ffmpeg on
PATH and in scoop and Chocolatey installs.

## Wallpaper Engine wallpapers

If you own [Wallpaper Engine](https://store.steampowered.com/app/431960/Wallpaper_Engine/), Looppaper can
use the wallpapers you subscribed to in its Steam Workshop. Looppaper only reads the Workshop downloads
already on your PC; it never downloads, uploads or redistributes Workshop content.

The Workshop folder is found through Steam (`HKCU\Software\Valve\Steam` and every library in
`steamapps\libraryfolders.vdf`, then `steamapps\workshop\content\431960`). If yours is somewhere else, set
`wallpaperEngine.workshopPath` (and `wallpaperEngine.installPath` for the folder with `wallpaper64.exe`) in
`config.json`.

```
Looppaper.exe --we-list
 #  Workshop id  Type       Size  Status            Title
 1  2530355779   scene    8.5 MB  needs conversion  Astronaut
 2  3592016945   scene   21.5 MB  needs conversion  Mystery wizard
```

The list is sorted by type (video, scene, web, application), then title, so the numbers stay the same
until you subscribe to or remove a wallpaper. Statuses:

| Status | Meaning |
|---|---|
| ready | an `.mp4` video; compressed once (see [Video compression](#video-compression)) and played |
| needs conversion | a scene or web wallpaper; it is recorded to a looping `.mp4` first |
| converted | a scene or web wallpaper already recorded (in `%LOCALAPPDATA%\Looppaper\cache`) |
| unsupported | an application wallpaper, a video that is not `.mp4`, or a broken item |

To use one, run `Looppaper.exe --we-set <number or Workshop id>`, or `Looppaper.exe --we-pick` to see the
list and type a number. Looppaper writes `"video": { "enabled": true, "file": "<full path>" }` into
`config.json` (your other settings are kept; comments in the file are not) and restarts a running Looppaper
so the new wallpaper shows straight away. Add `--no-restart` to only change the config.

A video wallpaper is compressed right there, like `--compress` (an existing compressed copy is reused), and
then set; the original Workshop file is never changed.

### Scene and web wallpapers

Scene and web wallpapers are rendered live by Wallpaper Engine, so Looppaper records them once into a
looping `.mp4` and plays that. `--we-set` / `--we-pick` on such an item explains what will happen, with an
estimate, and asks `Convert now? [y/N]`; `--we-convert <number|id>` only records it (without changing
`config.json`). While recording, Wallpaper Engine plays the wallpaper in a window of its own that is moved
off-screen (on PCs that don't render off-screen windows it is moved on-screen for the recording), and ffmpeg
records that window with its `gfxcapture` source. If Wallpaper Engine isn't running, Looppaper starts it for
the recording and closes it again afterwards; while it runs it may show its own wallpaper on the desktop.

- Length: if the scene's timeline animations loop (or a web page is built around one looping video), exactly
  one loop is recorded; otherwise 30 seconds whose end is crossfaded into the start so it loops seamlessly.
- Picture: your primary monitor's resolution at up to 30 fps; a scene with a different aspect ratio is
  rendered whole and cropped at the alignment position you set in Wallpaper Engine.
- Encoder: the first hardware encoder that works (NVIDIA NVENC, AMD AMF, Intel QSV), else the CPU (x264),
  at the same quality target as [video compression](#video-compression) (so the recording is already compressed;
  it is not encoded a second time), aiming to keep the file under 100 MB (a larger result is kept, with a size warning).
- Result: `%LOCALAPPDATA%\Looppaper\cache\we-<Workshop id>-<modified time>.mp4`. When the Workshop item is
  updated it is recorded again; deleting the cache folder is always safe. Ctrl+C cancels and saves nothing.

Application wallpapers are programs, not media, and are listed as unsupported.

Requirements:

- Wallpaper Engine installed (for scene and web wallpapers; video wallpapers only need the files).
- ffmpeg 8 or newer for converting: `winget install Gyan.FFmpeg` (or set `ffmpegPath`). ffmpeg is never
  bundled with Looppaper.

The conversion is a port of [wp2video](https://github.com/unio207/wp2video) (MIT License); see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Logs

`%LOCALAPPDATA%\Looppaper\logs\looppaper.log` (rolls over at 1 MB, one previous file kept as `looppaper.log.1`).

## Releases & updates

### Update check

Looppaper never updates itself. `Looppaper.exe --check-update` asks GitHub once
(`https://api.github.com/repos/DenKu1/Looppaper/releases/latest`) and prints whether a newer release
exists, with its link; it also writes the result to the log. With `"checkForUpdates": true` in
`config.json` (off by default) the wallpaper does the same once at start on a background thread with a
10-second timeout and only logs the result; a failed check never affects the wallpaper. Versions are
compared semantically (`v0.10.0` > `v0.9.0`, `0.2.0-beta.1` < `0.2.0`); pre-releases are only offered
to someone already running a pre-release. Nothing is downloaded or installed: get the new zip from the
release page and run its `install.ps1` again.

The check is anonymous, so it cannot see the releases of a private repository: GitHub answers 404 and
Looppaper reports "No public releases found" (not an error). It works once the repository is public.

### Cutting a release

Releases are built by `.github/workflows/release.yml` when a version tag is pushed:

```powershell
git tag v0.2.0 && git push origin v0.2.0
```

The workflow builds, tests and publishes the self-contained exe with the tag's version stamped in
(`-p:Version=0.2.0`; `Directory.Build.props` only holds the default for local builds), packs
`Looppaper-0.2.0-win-x64.zip` (exe, `install.ps1`, `uninstall.ps1`, README and any license / notice
files) and `Looppaper-0.2.0-win-x64.zip.sha256`, and creates a GitHub Release with both attached and
generated release notes. A tag with a suffix such as `v0.3.0-beta.1` becomes a pre-release. Running the
workflow by hand from the Actions tab is a dry run: it uploads the zip as a workflow artifact and
creates no release. To pack locally: `dotnet publish src/Looppaper -p:PublishProfile=win-x64 -p:Version=0.2.0`,
then `powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.2.0` (writes to `artifacts\`).

Check a download with `Get-FileHash Looppaper-0.2.0-win-x64.zip` against the `.sha256` file.

### Code signing

Releases are unsigned by default, so Windows SmartScreen may warn on first run ("More info" ->
"Run anyway"). The release workflow signs `Looppaper.exe` with `signtool` (SHA-256, RFC 3161 timestamp)
before packing when both of these repository secrets exist, and skips signing otherwise:

| Secret | Value |
|---|---|
| `SIGNING_CERT_BASE64` | The code-signing certificate as a base64-encoded `.pfx`: `[Convert]::ToBase64String([IO.File]::ReadAllBytes('cert.pfx'))` |
| `SIGNING_CERT_PASSWORD` | The `.pfx` password |

This needs an OV or EV code-signing certificate from a CA. Since 2023 new certificates are issued on
hardware tokens / cloud HSMs and usually cannot be exported as a `.pfx`; in that case use
[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/) instead: `release.yml` has a
commented-out step for it (secrets `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, plus the
account and certificate profile names). Signing changes the exe, so the zip and `.sha256` are always
made from the signed file.

### winget

`packaging/winget/templates/` holds the manifest template (schema 1.6: version, installer and en-US
default-locale files) for the package `DenKu1.Looppaper`: a `zip` installer with the nested portable
`Looppaper.exe`, exposed as the command `looppaper`. Portable packages install per user (no admin)
under `%LOCALAPPDATA%\Microsoft\WinGet\Packages`. After a release, fill it in with:

```powershell
.\packaging\winget\New-WingetManifest.ps1 -Version 0.2.0 -Validate   # -> packaging\winget\out\manifests\d\DenKu1\Looppaper\0.2.0
```

It takes the SHA-256 from the release's `.sha256` file (or `-ZipPath` / `-Sha256`) and runs
`winget validate`. Nothing is submitted: publishing means opening a pull request on
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) yourself (e.g. `wingetcreate submit <folder>`).

> **winget needs a publicly downloadable installer URL.** While this repository is private its release
> assets cannot be downloaded anonymously, so the winget package cannot work (and the script cannot
> fetch the `.sha256`) until the repository, or at least its releases, are public.

A winget install does not run `install.ps1`: run `looppaper --install-startup` once to start it at sign-in,
and `looppaper --stop` then `looppaper --uninstall-startup` before `winget uninstall DenKu1.Looppaper`.

## Build

Requires the .NET 8 SDK (or newer).

```powershell
dotnet build
dotnet test
dotnet publish src/Looppaper -p:PublishProfile=win-x64   # -> publish\Looppaper.exe (self-contained, single file)
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

CI (`.github/workflows/build.yml`) builds, tests and uploads `Looppaper-win-x64.zip` (exe + scripts + README) on every push.

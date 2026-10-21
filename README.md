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

   This copies `Looppaper.exe` to `%LOCALAPPDATA%\Programs\Looppaper`, creates
   `%USERPROFILE%\Pictures\Looppaper`, registers Looppaper to start when you sign in, and starts it.
3. Put an `.mp4` video, or `.jpg` / `.jpeg` / `.png` pictures, into `Pictures\Looppaper`.
   If the folder has a video (and video is enabled) it plays looped, muted by default; otherwise the
   pictures are shown. Looppaper rescans the folder at every slideshow step; after adding a video or
   editing `config.json`, restart it (`Looppaper.exe --stop`, then start it again).

To remove it: `powershell -ExecutionPolicy Bypass -File .\uninstall.ps1` (add `-RemoveSettings` to
also delete the config and logs). Your pictures and videos are never deleted.

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

On first run Looppaper writes `%APPDATA%\Looppaper\config.json` with the defaults below. The file
may contain `//` and `/* */` comments and trailing commas. Invalid values are clamped (with a warning
in the log); a file that is not valid JSON is ignored and the defaults are used.

```jsonc
{
  "collectionFolder": "%USERPROFILE%\\Pictures\\Looppaper", // pictures and videos
  "mode": "slideshow",          // "slideshow" or "single"
  "picture": null,              // single mode: file name in the collection, or a full path
  "intervalSeconds": 300,       // slideshow interval, 5..604800
  "shuffle": false,             // random slideshow order
  "scaling": "fill",            // "fill" | "fit" | "stretch" | "center"
  "backgroundColor": "#000000", // shown around "fit"/"center" pictures and when there is nothing to show
  "maxPictureSizeMb": 50,       // larger pictures are skipped (cap 200)
  "ffmpegPath": null,          // ffmpeg.exe or its folder; null = search PATH, winget, scoop, Chocolatey (only needed for conversion)
  "video": {
    "enabled": true,            // false = always show pictures
    "file": null,               // file name in the collection, or a full path; null = first .mp4 in the collection
    "volume": 0,                // 0..100; 0 = muted
    "maxFileSizeMb": 100        // larger videos are refused (logged) and pictures are shown; 100 is also the maximum
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
```

Command-line options override the config file for that run only; the file is not changed.

Commands such as `--help` and `--stop` print their result in the terminal you run them from. Starting the wallpaper from a terminal hands it to a background copy and returns straight away, so you can close the terminal. Started from Explorer or at sign-in no console window appears (Windows 11 24H2 and later; older Windows may flash a console window for a moment).

## Logs

`%LOCALAPPDATA%\Looppaper\logs\looppaper.log` (rolls over at 1 MB, one previous file kept as `looppaper.log.1`).

## Releases & updates

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

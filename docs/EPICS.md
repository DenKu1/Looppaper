# Looppaper roadmap

Looppaper is a lightweight live-wallpaper engine for Windows with no UI: it draws pictures
or a looping video behind the desktop icons, and is configured through `config.json`
and command-line switches.

## Epic 1 — Core wallpaper engine (MVP)

Acceptance criteria:

- Static pictures: `.jpeg`, `.jpg`, `.png`.
- Looping `.mp4` video wallpaper that can be enabled/disabled, with adjustable volume (muted by default);
  files above the 100 MB limit (default and hard cap) are refused and logged, and pictures are shown instead.
- Starts on boot (per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, no admin).
- Configurable collection folder.
- Single-picture mode, or cycle through all pictures with a configurable interval.
- C#, no UI (no settings window; `config.json` + command-line switches), easy install.
- Low CPU / GPU / RAM use.

Tasks:

- [x] 1.1 Scaffold: solution, projects, `.gitignore`, README, this roadmap
- [x] 1.2 Configuration + logging (`config.json`, CLI switches, rolling log)
- [x] 1.3 Media collection (folder scan, size limits, ordering, shuffle)
- [x] 1.4 Desktop host (WorkerW / Progman parenting, wallpaper restore on exit)
- [x] 1.5 Picture renderer (decode once, pre-scale per monitor, BitBlt cache)
- [x] 1.6 Single picture + slideshow (timer, background preparation, empty-folder handling)
- [x] ~~1.7 MP3 playback (Windows MCI, looping playlist, shuffle, volume)~~ — removed, replaced by 1.12
- [x] 1.8 Single instance + `--stop`
- [x] 1.9 Start on boot (`--install-startup` / `--uninstall-startup`)
- [x] 1.10 Easy install (self-contained single-file publish, install/uninstall scripts, CI)
- [x] 1.11 Performance pass (priority, EcoQoS, working-set trimming, measured budget)
- [x] 1.12 MP4 video wallpaper (Media Foundation hardware decode, looped, on/off, volume; replaces MP3)
- [x] 1.13 Video size limit hardened to 100 MB (default and hard cap; larger videos refused, pictures shown)

## Epic 2 — Robustness & power awareness

- [ ] Explorer restart recovery (re-attach on `TaskbarCreated`)
- [ ] Pause on fullscreen app / game
- [ ] Pause on lock, sleep and battery saver
- [ ] Monitor hot-plug and DPI changes
- [ ] Hot-reload of config + collection folder watcher
- [ ] Crash watchdog that restores the original wallpaper

## Shared groundwork for Epics 8 and 9

- [x] S.1 Console-subsystem exe with the "detached" console allocation policy: commands print and prompt in the
      terminal (which waits for them); the wallpaper itself runs as a background copy with no console window.
- [x] S.2 One ffmpeg locator (config `ffmpegPath`, PATH, winget, scoop, Chocolatey, common folders) with version check,
      an ffprobe reader and a runner that reports progress.
- [x] S.3 Shared cache folder for converted / compressed videos: `%LOCALAPPDATA%\Looppaper\cache` (`AppPaths.CacheDirectory`).

## Epic 8 — Wallpaper Engine library (next up)

Use wallpapers the user already has from the Wallpaper Engine Steam Workshop. Looppaper only reads
the local Workshop downloads (it never downloads or redistributes Workshop content), and Wallpaper
Engine itself is needed for converting scene wallpapers.

- [x] 8.1 Find the Workshop folder: Steam path from `HKCU\Software\Valve\Steam` (`SteamPath`) plus every
      library in `steamapps\libraryfolders.vdf`, then `steamapps\workshop\content\431960`. Config override
      `"wallpaperEngine": { "workshopPath": null, "installPath": null }`.
- [x] 8.2 Read each item's `project.json` (title, type video / scene / web / application, file) and list them
      in the console with a number, Workshop id, type, title, size and status (ready / needs conversion /
      too large / unsupported): `--we-list`.
- [ ] 8.3 Choose one from the console: `--we-set <number|id>`, plus an interactive picker (`--we-pick`) that
      prompts for a number. Updates `config.json` and restarts a running Looppaper.
- [ ] 8.4 Video items: use the `.mp4` directly when it is within the 100 MB limit; larger ones go through
      Epic 9 (oversized video compression).
- [ ] 8.5 Scene (and web) items: convert to a looping `.mp4` before playing, porting the approach of
      [unio207/wp2video](https://github.com/unio207/wp2video) (MIT): run `wallpaper64.exe -control openWallpaper
      -playInWindow`, move the window off-screen, record it with ffmpeg 8+ `gfxcapture`, loop length from
      `scene.json` / `scene.pkg`, optional seamless crossfade, hardware encoder probing (NVENC / AMF / QSV, CPU
      fallback), output under 100 MB, cached by Workshop id + modified time in `%LOCALAPPDATA%\Looppaper\cache`.
      Needs Wallpaper Engine installed and ffmpeg 8+ (PATH, winget or a config path); ffmpeg is never committed
      or bundled. Application wallpapers are listed as unsupported.
- [ ] 8.6 Credit: `THIRD-PARTY-NOTICES.md` with wp2video's full MIT license text and a link, referenced from the
      README, added together with the first ported code.

## Epic 9 — Oversized video compression

Videos are limited to 100 MB. Instead of only refusing a larger one, offer to compress it — always
asking first.

- [x] 9.1 Detect: when a chosen video (config, `--video`, collection folder or a Wallpaper Engine item) is over
      100 MB, don't play it; log it and fall back to pictures (current behaviour).
- [x] 9.2 Notify and ask first: show the file name, its size and the estimated result, and ask before
      compressing. Console prompt when run from a terminal (`--compress <file>`, or the `--we-set` flow asks
      "Compress to fit under 100 MB? [y/N]"); when started in the background with no console, show a Windows
      notification or message box asking yes / no. Never compress silently. Remember a "no" per file so it does
      not ask on every start.
- [ ] 9.3 Compress with ffmpeg (PATH, winget or config path; not bundled): target under 100 MB with a bitrate
      computed from the duration, scale down to screen resolution if larger, hardware encoder (NVENC / AMF / QSV)
      with CPU fallback, drop audio only if needed to fit. Write to `%LOCALAPPDATA%\Looppaper\cache`, never
      overwrite the original. Show progress in the console.
- [ ] 9.4 Use the result: verify it is under 100 MB, then play it and record the mapping so it is not
      compressed again.
- [ ] 9.5 Tests and README.
## Epic 6 — Distribution

- [x] 6.1 Release workflow: pushing a tag `vX.Y.Z` builds, tests and publishes with the tag's version, packs
      `Looppaper-X.Y.Z-win-x64.zip` + `.sha256` and creates a GitHub Release (`.github/workflows/release.yml`).
- [x] 6.2 winget manifest template (schema 1.6, zip + nested portable exe, alias `looppaper`) and
      `packaging/winget/New-WingetManifest.ps1` to fill version / URL / SHA-256 from a release. Nothing is
      submitted to microsoft/winget-pkgs. winget needs a publicly downloadable installer URL, so it cannot
      work while the repository is private.
- [x] 6.3 Optional code signing in the release workflow: `signtool` with a timestamp server when the secrets
      `SIGNING_CERT_BASE64` + `SIGNING_CERT_PASSWORD` exist (skipped otherwise), and a commented Azure Trusted
      Signing alternative. Needs an OV/EV certificate or an Azure Trusted Signing account (owner's decision).
- [ ] 6.4 Opt-in update check (`checkForUpdates`, `--check-update`).

## Epic 7 — Optional control surface

- [ ] `--next` / `--pause` commands sent to the running instance
- [ ] Optional tray icon (off by default)

## Won't do (for now)

Parked; kept here so the ideas are not lost.

### Epic 3 — Animated wallpapers

- [ ] GIF with frame-rate cap
- [ ] WebM / other containers beyond `.mp4`; video playlists (rotate between several videos)
- [ ] One decoder shared across monitors (today each monitor decodes its own copy)
- [ ] APNG / animated WebP

### Epic 4 — Multi-monitor control

- [ ] Per-monitor picture
- [ ] Span one picture across all monitors
- [ ] Per-monitor scaling mode

### Epic 5 — Video audio extras

- [ ] Fade audio in / out on start and stop
- [ ] Ducking or muting when other apps play audio
- [ ] Media keys / global hotkeys (mute, volume)

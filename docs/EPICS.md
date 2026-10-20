# Looppaper roadmap

Looppaper is a lightweight live-wallpaper engine for Windows with no UI: it draws pictures
or a looping video behind the desktop icons, and is configured through `config.json`
and command-line switches.

## Epic 1 — Core wallpaper engine (MVP)

Acceptance criteria:

- Static pictures: `.jpeg`, `.jpg`, `.png`.
- Looping `.mp4` video wallpaper that can be enabled/disabled, with adjustable volume (muted by default);
  files above a size limit (default 500 MB, hard cap 4096 MB) are skipped and logged.
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

## Epic 2 — Robustness & power awareness

- [ ] Explorer restart recovery (re-attach on `TaskbarCreated`)
- [ ] Pause on fullscreen app / game
- [ ] Pause on lock, sleep and battery saver
- [ ] Monitor hot-plug and DPI changes
- [ ] Hot-reload of config + collection folder watcher
- [ ] Crash watchdog that restores the original wallpaper

## Epic 3 — Animated wallpapers

- [ ] GIF with frame-rate cap
- [ ] WebM / other containers beyond `.mp4`; video playlists (rotate between several videos)
- [ ] One decoder shared across monitors (today each monitor decodes its own copy)
- [ ] APNG / animated WebP

## Epic 4 — Multi-monitor control

- [ ] Per-monitor picture
- [ ] Span one picture across all monitors
- [ ] Per-monitor scaling mode

## Epic 5 — Video audio extras

- [ ] Fade audio in / out on start and stop
- [ ] Ducking or muting when other apps play audio
- [ ] Media keys / global hotkeys (mute, volume)

## Epic 6 — Distribution

- [ ] GitHub releases
- [ ] winget manifest
- [ ] Code signing
- [ ] Update check

## Epic 7 — Optional control surface

- [ ] `--next` / `--pause` commands sent to the running instance
- [ ] Optional tray icon (off by default)

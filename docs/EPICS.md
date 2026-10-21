# Looppaper roadmap

Looppaper is a lightweight live-wallpaper app for Windows with no UI: it draws pictures
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

Keep the wallpaper alive when the desktop changes under it, and stop spending power when nobody can see it.
Pausing covers both the video (Media Engine paused, so the GPU decoder idles) and the slideshow timer.

- [x] 2.1 Explorer restart recovery: a hidden top-level control window owns the message loop and receives
      `TaskbarCreated`; when Explorer restarts (or the wallpaper window's parent disappears) Looppaper finds the
      new desktop host, recreates its window and restarts the video or pictures instead of exiting.
- [x] 2.2 Pause on fullscreen app / game: while the foreground window covers its whole monitor (or Windows reports
      a full-screen Direct3D app or presentation mode) playback pauses, and resumes when that window goes away.
      Config `pause.onFullscreen` (default true).
- [x] 2.3 Pause on lock, sleep, display off and battery saver: session lock / disconnect (`pause.onLock`, default
      true), suspend / resume, the display turning off (always), Windows energy saver (`pause.onBatterySaver`,
      default true) and optionally running on battery (`pause.onBattery`, default false).
- [x] 2.4 Monitor hot-plug, resolution and DPI changes: on display, DPI or work-area changes (debounced) the window
      is resized to the new virtual screen and the video players / picture frame are rebuilt per monitor.
- [x] 2.5 Hot-reload of `config.json` and a collection folder watcher: edits to the config apply without a restart
      (command-line overrides still win); pictures or videos added, changed or removed in the collection folder are
      picked up straight away (debounced).
- [x] 2.6 Crash watchdog: a small `--watchdog` copy waits for the wallpaper process; if it ends without a clean stop
      (crash, killed) the original Windows wallpaper is re-applied, and after a crash Looppaper is started again (at
      most 3 times in 10 minutes). Config `watchdog` (default true).
- [x] 2.7 Tests and README.

## Shared groundwork for Epics 8 and 9

- [x] S.1 Console-subsystem exe with the "detached" console allocation policy: commands print and prompt in the
      terminal (which waits for them); the wallpaper itself runs as a background copy with no console window.
- [x] S.2 One ffmpeg locator (config `ffmpegPath`, PATH, winget, scoop, Chocolatey, common folders) with version check,
      an ffprobe reader and a runner that reports progress.
- [x] S.3 Shared cache folder for converted / compressed videos: `%LOCALAPPDATA%\Looppaper\cache` (`AppPaths.CacheDirectory`).

## Epic 8 — Wallpaper Engine library (done)

Use wallpapers the user already has from the Wallpaper Engine Steam Workshop. Looppaper only reads
the local Workshop downloads (it never downloads or redistributes Workshop content), and Wallpaper
Engine itself is needed for converting scene wallpapers.

- [x] 8.1 Find the Workshop folder: Steam path from `HKCU\Software\Valve\Steam` (`SteamPath`) plus every
      library in `steamapps\libraryfolders.vdf`, then `steamapps\workshop\content\431960`. Config override
      `"wallpaperEngine": { "workshopPath": null, "installPath": null }`.
- [x] 8.2 Read each item's `project.json` (title, type video / scene / web / application, file) and list them
      in the console with a number, Workshop id, type, title, size and status (ready / needs conversion /
      too large / unsupported): `--we-list`.
- [x] 8.3 Choose one from the console: `--we-set <number|id>`, plus an interactive picker (`--we-pick`) that
      prompts for a number. Updates `config.json` and restarts a running Looppaper.
- [x] 8.4 Video items: use the `.mp4` directly when it is within the 100 MB limit; larger ones go through
      Epic 9 (oversized video compression).
- [x] 8.5 Scene (and web) items: convert to a looping `.mp4` before playing, porting the approach of
      [unio207/wp2video](https://github.com/unio207/wp2video) (MIT): run `wallpaper64.exe -control openWallpaper
      -playInWindow`, move the window off-screen, record it with ffmpeg 8+ `gfxcapture`, loop length from
      `scene.json` / `scene.pkg`, optional seamless crossfade, hardware encoder probing (NVENC / AMF / QSV, CPU
      fallback), output under 100 MB, cached by Workshop id + modified time in `%LOCALAPPDATA%\Looppaper\cache`.
      Needs Wallpaper Engine installed and ffmpeg 8+ (PATH, winget or a config path); ffmpeg is never committed
      or bundled. Application wallpapers are listed as unsupported.
- [x] 8.6 Credit: `THIRD-PARTY-NOTICES.md` with wp2video's full MIT license text and a link, referenced from the
      README, added together with the first ported code.

## Epic 9 — Oversized video compression

> Superseded by 10.2: there is no 100 MB refusal any more and Looppaper no longer asks; it compresses every
> video by default and tells the person, warning only when a compressed video is still over 100 MB.

Videos are limited to 100 MB. Instead of only refusing a larger one, offer to compress it — always
asking first.

- [x] 9.1 Detect: when a chosen video (config, `--video`, collection folder or a Wallpaper Engine item) is over
      100 MB, don't play it; log it and fall back to pictures (current behaviour).
- [x] 9.2 Notify and ask first: show the file name, its size and the estimated result, and ask before
      compressing. Console prompt when run from a terminal (`--compress <file>`, or the `--we-set` flow asks
      "Compress to fit under 100 MB? [y/N]"); when started in the background with no console, show a Windows
      notification or message box asking yes / no. Never compress silently. Remember a "no" per file so it does
      not ask on every start.
- [x] 9.3 Compress with ffmpeg (PATH, winget or config path; not bundled): target under 100 MB with a bitrate
      computed from the duration, scale down to screen resolution if larger, hardware encoder (NVENC / AMF / QSV)
      with CPU fallback, drop audio only if needed to fit. Write to `%LOCALAPPDATA%\Looppaper\cache`, never
      overwrite the original. Show progress in the console.
- [x] 9.4 Use the result: verify it is under 100 MB, then play it and record the mapping so it is not
      compressed again.
- [x] 9.5 Tests and README.
## Epic 10 — Install folder choice & compression by default

- [x] 10.1 "Everything in one folder" layout: config, collection, cache, compression records and logs in the
      install folder (a `looppaper.portable` marker next to the exe switches it on). The first start in that layout
      copies the old config, cache, compression records and default collection (originals kept). Uninstall keeps
      settings and the collection unless asked to remove them.
- [x] 10.1b The installer asks which layout to use; the default is the standard Windows-style split (config in
      %APPDATA%, collection in Pictures, cache and logs in %LOCALAPPDATA%); `-Layout Standard|Portable` skips the question.
- [x] 10.2 Compress every video by default (`video.compress`, default true), telling the user instead of asking;
      no 100 MB refusal any more — a compressed video still over 100 MB only gets a performance warning.
- [x] 10.3 Scene / web conversions record at the same quality target as video compression, so they are already
      compressed and never encoded twice.
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
- [x] 6.4 Opt-in update check: `"checkForUpdates": false` by default (true = check once at start in the
      background and log it) and `--check-update` (check once, print). Semantic version compare and release
      JSON parsing in Core with tests; never downloads. A private repository's releases are invisible without
      a token, so it reports "no public releases found" until the repository is public.

## Epic 7 — Optional control surface

Control the running wallpaper without a settings window: commands from a terminal (or a shortcut / script), and
a notification-area icon the installer asks about (on by default). Commands reach the running instance through a
registered window message to its hidden control window and print what it is doing afterwards.

- [x] 7.1 `--pause`, `--resume` and `--status` sent to the running instance. `--pause` is a pause reason of its own
      next to the automatic ones (full-screen, lock, battery saver, ...), so `--resume` only lifts the user's pause and
      playback still waits for the automatic ones; it lasts until `--resume` or Looppaper is restarted.
- [x] 7.2 `--next` / `--previous`: switch the slideshow to the next / previous picture now (the interval starts
      again; previous goes back through the pictures already shown, also when shuffled). Single-picture mode and video
      say there is nothing to switch to.
- [x] 7.3 Tray icon (`"trayIcon": true` by default; the installer asks, `-TrayIcon On|Off` skips the question;
      `--tray-icon on|off` changes it later and a running Looppaper follows straight away): left or right click opens a
      menu with next / previous picture, pause / resume, open the collection folder, edit config.json, open the log and
      stop Looppaper; the icon shows play or pause and its tooltip what is shown; it comes back after Explorer restarts.
- [x] 7.4 Tests and README.

## Epic 11 — Make the repository public

Public since 2026-10-21.

- [x] 11.1 Choose and add a LICENSE for Looppaper (the owner decides; MIT is the simple option and is compatible
      with the wp2video-derived code). Update the winget manifest's `License` field to match. MIT, with
      `License` / `LicenseUrl` in the winget locale template.
- [x] 11.2 Scan the full git history for secrets and tokens, personal paths (e.g. `C:\Users\<name>`), personal
      email addresses, test media, Wallpaper Engine / Workshop content that can't be redistributed, and anything
      that doesn't belong in a public repo. Fix it, rewriting history only with the owner's OK. Clean, no rewrite
      needed: every commit uses the GitHub no-reply address, no blob is over 50 KB (no media), the only user paths
      are placeholders (`C:\Users\you`, `C:\Users\me`), secrets are only referenced as `${{ secrets.* }}`, and
      Workshop items appear only as ids and titles in tests and README examples.
- [x] 11.3 Check third-party licensing: wp2video (MIT) credited in `THIRD-PARTY-NOTICES.md`; ffmpeg not bundled;
      no Workshop assets in the repo or the release zip. wp2video's LICENSE is MIT (matches the copied text) and
      the three ported files carry a header; the release zip and the CI artifact now include `LICENSE` and
      `THIRD-PARTY-NOTICES.md`.
- [x] 11.4 README for outside users: what it is, requirements (Windows 10/11; ffmpeg for conversion and
      compression; Wallpaper Engine for scenes), install, usage, screenshots / GIF, known limitations.
      No screenshot / GIF (the owner's choice).
- [x] 11.5 `CONTRIBUTING.md`, issue and pull-request templates, `SECURITY.md`, optional code of conduct.
      No code of conduct for now. `SECURITY.md` points to GitHub private vulnerability reporting, which has to be
      turned on (done with 11.7).
- [x] 11.6 Turn on what public visibility makes possible: free CI minutes, the update check (`--check-update`
      can see public releases), winget submission (6.2), optional free code signing (e.g. SignPath for open source).
      Done: private vulnerability reporting is on, CI runs on the free public minutes, and v0.2.0 is released (the
      update check sees it; the winget manifest validates). winget submission and free signing moved to Epic 12.
- [x] 11.7 Switch the repository to public. Done by the owner, or by an assistant only with the owner's explicit
      go-ahead; always the last step. Switched on 2026-10-21 with the owner's go-ahead.

## Epic 13 — Default wallpaper, video folder & video cycling (done)

A new install shows something straight away, everything converted from Wallpaper Engine stays in the collection,
and the slideshow switches between videos too.

Owner's decisions (2026-10-21): all three parts approved; cycling is videos only (pictures only when there is no
video). The log warnings about `D:\path\your.mp4` and `C:\Users\You\Videos\my-video.mp4` came from manual test
commands with example paths, not from Looppaper, so there is nothing to fix there.

- [x] 13.1 Built-in default wallpaper: when there is no picture or video to show, Looppaper draws its own
      wallpaper (a gradient with the name and where to put pictures and videos) instead of the plain background
      color. Nothing is written to the collection.
- [x] 13.2 A video folder: `video.folder`, `%USERPROFILE%\Videos\Looppaper` in the standard layout and the
      install folder's `collection` in the one-folder layout. Videos are found there and, as before, in the
      collection folder. The installer creates it, the tray menu opens it, uninstall `-RemoveCollection` covers
      it, and the one-folder migration copies it.
- [x] 13.3 Wallpaper Engine results go to the video folder with readable names
      (`<title> (we-<id>-<time>).mp4`): scene / web recordings, and the (compressed) copy of a video wallpaper,
      so every pick stays in the collection. Older recordings in the cache are moved there (with `config.json`
      and the compression records updated) the next time a `--we-*` command runs.
- [x] 13.4 Video cycling: in slideshow mode several videos take turns every `intervalSeconds` (shuffled with
      `shuffle`), starting with `video.file` when it is set; single mode keeps playing one video. `--next` /
      `--previous` and the tray menu switch videos too. Pictures are shown only when there is no video.
- [x] 13.5 Tests and README. A video that stops after playing a while (e.g. the GPU ran out of memory) is started
      again; one that fails right away is skipped for the next video, and pictures show only when none plays.

## Won't do (for now)

Parked; kept here so the ideas are not lost.

### Epic 3 — Animated wallpapers

- [ ] GIF with frame-rate cap
- [ ] WebM / other containers beyond `.mp4` (video playlists came with Epic 13)
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

### Epic 12 — Store and signing

Each of these adds something the owner has to keep doing after every release, so they are parked.

- [ ] Submit `DenKu1.Looppaper` to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) (the manifest
      from `New-WingetManifest.ps1` validates for 0.2.0), then one manifest pull request per release. Needs the
      Microsoft contributor agreement signed once; published release zips must never be replaced (winget checks the
      SHA-256).
- [ ] Free code signing through the [SignPath Foundation](https://signpath.org/apply): two-factor sign-in on GitHub,
      a "Code signing policy" section on the README and release page (credit, team roles, privacy note for the update
      check), a SignPath step in `release.yml`, and a manual approval for every signing request.

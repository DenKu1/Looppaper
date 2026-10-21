# Contributing to Looppaper

Thanks for helping. Looppaper is a small personal project maintained in spare time, so there is no promise
that issues or pull requests get an answer.

## Reporting a bug

Open an [issue](https://github.com/DenKu1/Looppaper/issues/new/choose) with the bug template and include:

- your Windows version (`winver`) and the Looppaper version (from the release zip name, or `--check-update`);
- what you expected and what happened instead;
- the relevant part of `%LOCALAPPDATA%\Looppaper\logs\looppaper.log` (or `logs\` next to `Looppaper.exe`
  in the everything-in-one-folder layout), and your `config.json` if it matters.

Check the log for your user name or folder names before you paste it.

## Suggesting a feature

Open an issue with the feature template. Looppaper deliberately has no settings window and aims to cost
almost nothing while it runs, so ideas that keep it light fit best. [docs/EPICS.md](docs/EPICS.md) lists
what is planned and what is parked.

## Pull requests

For anything bigger than a small fix, open an issue first so we can agree on the approach.

Build and test with the .NET 8 SDK (or newer) on Windows:

```powershell
dotnet build
dotnet test
```

- Keep pull requests focused on one change, and add or update tests in `tests/Looppaper.Core.Tests` where
  the logic lives in `Looppaper.Core`.
- The build treats warnings as errors; please keep it clean.
- Match the style of the surrounding code, and update `README.md` when behaviour or options change.
- Do not add Wallpaper Engine / Steam Workshop content, test videos or other media you may not
  redistribute. Test media belongs in the ignored `test_video/` folder.

By contributing you agree that your contribution is released under the [MIT License](LICENSE).

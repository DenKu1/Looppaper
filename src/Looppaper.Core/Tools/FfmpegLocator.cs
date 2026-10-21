using System.Globalization;
using System.Text.RegularExpressions;
using Looppaper.Core.Logging;

namespace Looppaper.Core.Tools;

/// <summary>An ffmpeg found on this machine. ffmpeg is never bundled with Looppaper.</summary>
public sealed record FfmpegTool(string FfmpegPath, string? FfprobePath, FfmpegVersion Version);

/// <summary>What the locator needs from the machine; replaced by a fake in tests.</summary>
public interface IToolEnvironment
{
    string? GetEnvironmentVariable(string name);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    IEnumerable<string> EnumerateDirectories(string path, string searchPattern);

    /// <summary>Runs <c>exe -version</c> and returns the first line, or null when it cannot be run.</summary>
    string? ReadVersionLine(string exe);
}

/// <summary>
/// Finds ffmpeg (and ffprobe next to it): the configured <c>ffmpegPath</c> first, then PATH, winget
/// (links and the Gyan.FFmpeg / BtbN packages), scoop, Chocolatey and a few common folders.
/// </summary>
public sealed class FfmpegLocator
{
    private const string Exe = "ffmpeg.exe";

    private readonly IToolEnvironment _env;

    public FfmpegLocator(IToolEnvironment? environment = null)
    {
        _env = environment ?? new SystemToolEnvironment();
    }

    /// <summary>Every place ffmpeg is looked for, in order (may contain paths that do not exist).</summary>
    public IEnumerable<string> CandidatePaths(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configured = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
            if (configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                yield return configured;
            }
            else
            {
                yield return Path.Combine(configured, Exe);
                yield return Path.Combine(configured, "bin", Exe);
            }
        }

        foreach (var dir in (_env.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(dir.Trim('"'), Exe);
        }

        var localAppData = _env.GetEnvironmentVariable("LOCALAPPDATA");
        var programFiles = _env.GetEnvironmentVariable("ProgramFiles");
        var programData = _env.GetEnvironmentVariable("ProgramData");
        var userProfile = _env.GetEnvironmentVariable("USERPROFILE");

        if (localAppData is not null)
        {
            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", Exe);
            foreach (var path in WingetPackages(Path.Combine(localAppData, "Microsoft", "WinGet", "Packages")))
            {
                yield return path;
            }
        }

        if (programFiles is not null)
        {
            foreach (var path in WingetPackages(Path.Combine(programFiles, "WinGet", "Packages")))
            {
                yield return path;
            }
        }

        if (userProfile is not null)
        {
            yield return Path.Combine(userProfile, "scoop", "shims", Exe);
            yield return Path.Combine(userProfile, "scoop", "apps", "ffmpeg", "current", "bin", Exe);
        }

        if (programData is not null)
        {
            yield return Path.Combine(programData, "chocolatey", "bin", Exe);
        }

        yield return @"C:\ffmpeg\bin\" + Exe;
        if (programFiles is not null)
        {
            yield return Path.Combine(programFiles, "ffmpeg", "bin", Exe);
        }
    }

    /// <summary>
    /// Returns the first working ffmpeg that is at least <paramref name="minimumMajor"/> (when given),
    /// or null. Each rejected candidate is logged.
    /// </summary>
    public FfmpegTool? Find(string? configuredPath, ILog log, int? minimumMajor = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in CandidatePaths(configuredPath))
        {
            if (!seen.Add(candidate) || !_env.FileExists(candidate))
            {
                continue;
            }

            var line = _env.ReadVersionLine(candidate);
            if (line is null)
            {
                log.Warn($"ffmpeg at {candidate} could not be run");
                continue;
            }

            var version = FfmpegVersion.Parse(line);
            if (minimumMajor is { } major && !version.IsAtLeast(major))
            {
                log.Warn($"ffmpeg at {candidate} is {version}; version {major} or newer is needed");
                continue;
            }

            var probe = Path.Combine(Path.GetDirectoryName(candidate) ?? string.Empty, "ffprobe.exe");
            return new FfmpegTool(candidate, _env.FileExists(probe) ? probe : null, version);
        }

        return null;
    }

    /// <summary>Install hint shown when ffmpeg is missing.</summary>
    public const string InstallHint =
        "ffmpeg was not found. Install it with \"winget install Gyan.FFmpeg\" (then open a new terminal), " +
        "or set \"ffmpegPath\" in config.json to ffmpeg.exe or its folder.";

    private IEnumerable<string> WingetPackages(string packagesDir)
    {
        if (!_env.DirectoryExists(packagesDir))
        {
            yield break;
        }

        foreach (var package in _env.EnumerateDirectories(packagesDir, "*FFmpeg*").OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
        {
            // Gyan.FFmpeg unpacks to <package>\ffmpeg-<version>-full_build\bin\ffmpeg.exe.
            foreach (var build in _env.EnumerateDirectories(package, "*").OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(build, "bin", Exe);
            }

            yield return Path.Combine(package, "bin", Exe);
        }
    }
}

/// <summary>Version from the first line of <c>ffmpeg -version</c>.</summary>
public sealed partial record FfmpegVersion(string Text, Version? Number, DateOnly? BuildDate)
{
    /// <summary>ffmpeg 8.0 was released on 2025-08-22; dated git builds from then on include its features.</summary>
    private static readonly DateOnly Release8 = new(2025, 8, 22);

    public static FfmpegVersion Parse(string versionLine)
    {
        var match = NumberPattern().Match(versionLine);
        if (match.Success)
        {
            var build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            var number = new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), build);
            return new FfmpegVersion(match.Groups[0].Value["version ".Length..], number, null);
        }

        var dated = DatePattern().Match(versionLine);
        if (dated.Success && DateOnly.TryParseExact(dated.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new FfmpegVersion(dated.Groups[0].Value["version ".Length..], null, date);
        }

        var text = versionLine.StartsWith("ffmpeg version ", StringComparison.Ordinal)
            ? versionLine["ffmpeg version ".Length..].Split(' ')[0]
            : versionLine;
        return new FfmpegVersion(text, null, null);
    }

    /// <summary>
    /// True when this is at least <paramref name="major"/>.0. Dated git builds compare by date (only 8 is
    /// known); nightly builds without a date ("N-...") are assumed to be recent.
    /// </summary>
    public bool IsAtLeast(int major)
    {
        if (Number is not null)
        {
            return Number.Major >= major;
        }

        if (BuildDate is { } date)
        {
            return major <= 7 || (major == 8 && date >= Release8);
        }

        return Text.StartsWith("N-", StringComparison.Ordinal);
    }

    public override string ToString() => Text;

    [GeneratedRegex(@"version n?(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"version (\d{4}-\d{2}-\d{2})-git\S*")]
    private static partial Regex DatePattern();
}

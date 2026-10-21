using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Looppaper.Core.Logging;

namespace Looppaper.Core.Configuration;

/// <summary>
/// Reads and writes <c>config.json</c>. Reading is deliberately lenient: comments and trailing
/// commas are allowed, property names are case-insensitive, a field with a wrong type keeps its
/// default (with a warning), and unparsable JSON yields the defaults. Loading never throws.
/// </summary>
public static class ConfigLoader
{
    private const string Header =
        "// Looppaper configuration. Comments and trailing commas are allowed.\r\n" +
        "// See https://github.com/DenKu1/Looppaper#configuration for every field.\r\n";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Loads the config, creating it with defaults when the file does not exist.</summary>
    public static LooppaperConfig LoadOrCreate(string path, ILog log)
    {
        if (!File.Exists(path))
        {
            var defaults = new LooppaperConfig();
            if (TrySave(path, defaults, log))
            {
                log.Info($"Created default config at {path}");
            }

            return defaults;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Cannot read config {path}; using defaults", ex);
            return new LooppaperConfig();
        }

        var config = Parse(text, log);
        ConfigValidator.Normalize(config, log);
        return config;
    }

    public static string Serialize(LooppaperConfig config) =>
        Header + JsonSerializer.Serialize(config, WriteOptions) + "\r\n";

    public static bool TrySave(string path, LooppaperConfig config, ILog log)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, Serialize(config), new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Cannot write config {path}", ex);
            return false;
        }
    }

    /// <summary>Parses config JSON without validating ranges (see <see cref="ConfigValidator"/>).</summary>
    public static LooppaperConfig Parse(string json, ILog log)
    {
        var config = new LooppaperConfig();
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: DocumentOptions);
        }
        catch (JsonException ex)
        {
            log.Error($"Config is not valid JSON ({ex.Message}); using defaults");
            return config;
        }

        if (root is not JsonObject obj)
        {
            log.Error("Config root must be a JSON object; using defaults");
            return config;
        }

        foreach (var (name, value) in obj)
        {
            switch (name.ToLowerInvariant())
            {
                case "collectionfolder":
                    if (ReadString(value, name, log, allowNull: false, out var folder))
                    {
                        config.CollectionFolder = folder!;
                    }

                    break;
                case "mode":
                    if (ReadEnum<WallpaperMode>(value, name, log, out var mode))
                    {
                        config.Mode = mode;
                    }

                    break;
                case "picture":
                    if (ReadString(value, name, log, allowNull: true, out var picture))
                    {
                        config.Picture = string.IsNullOrWhiteSpace(picture) ? null : picture;
                    }

                    break;
                case "intervalseconds":
                    if (ReadInt(value, name, log, out var interval))
                    {
                        config.IntervalSeconds = interval;
                    }

                    break;
                case "shuffle":
                    if (ReadBool(value, name, log, out var shuffle))
                    {
                        config.Shuffle = shuffle;
                    }

                    break;
                case "scaling":
                    if (ReadEnum<ScalingMode>(value, name, log, out var scaling))
                    {
                        config.Scaling = scaling;
                    }

                    break;
                case "backgroundcolor":
                    if (ReadString(value, name, log, allowNull: false, out var color))
                    {
                        config.BackgroundColor = color!;
                    }

                    break;
                case "maxpicturesizemb":
                    if (ReadInt(value, name, log, out var maxPicture))
                    {
                        config.MaxPictureSizeMb = maxPicture;
                    }

                    break;
                case "checkforupdates":
                    if (ReadBool(value, name, log, out var checkForUpdates))
                    {
                        config.CheckForUpdates = checkForUpdates;
                    }

                    break;
                case "ffmpegpath":
                    if (ReadString(value, name, log, allowNull: true, out var ffmpeg))
                    {
                        config.FfmpegPath = string.IsNullOrWhiteSpace(ffmpeg) ? null : ffmpeg;
                    }

                    break;
                case "video":
                    ParseVideo(value, config.Video, log);
                    break;
                case "wallpaperengine":
                    ParseWallpaperEngine(value, config.WallpaperEngine, log);
                    break;
                default:
                    log.Warn($"Config: unknown field '{name}' ignored");
                    break;
            }
        }

        return config;
    }

    private static void ParseVideo(JsonNode? node, VideoConfig video, ILog log)
    {
        if (node is null)
        {
            return;
        }

        if (node is not JsonObject obj)
        {
            log.Warn("Config: 'video' must be an object; using defaults");
            return;
        }

        foreach (var (name, value) in obj)
        {
            var path = "video." + name;
            switch (name.ToLowerInvariant())
            {
                case "enabled":
                    if (ReadBool(value, path, log, out var enabled))
                    {
                        video.Enabled = enabled;
                    }

                    break;
                case "file":
                    if (ReadString(value, path, log, allowNull: true, out var file))
                    {
                        video.File = string.IsNullOrWhiteSpace(file) ? null : file;
                    }

                    break;
                case "volume":
                    if (ReadInt(value, path, log, out var volume))
                    {
                        video.Volume = volume;
                    }

                    break;
                case "maxfilesizemb":
                    if (ReadInt(value, path, log, out var maxSize))
                    {
                        video.MaxFileSizeMb = maxSize;
                    }

                    break;
                default:
                    log.Warn($"Config: unknown field '{path}' ignored");
                    break;
            }
        }
    }
    private static void ParseWallpaperEngine(JsonNode? node, WallpaperEngineConfig section, ILog log)
    {
        if (node is null)
        {
            return;
        }

        if (node is not JsonObject obj)
        {
            log.Warn("Config: 'wallpaperEngine' must be an object; using defaults");
            return;
        }

        foreach (var (name, value) in obj)
        {
            var path = "wallpaperEngine." + name;
            switch (name.ToLowerInvariant())
            {
                case "workshoppath":
                    if (ReadString(value, path, log, allowNull: true, out var workshop))
                    {
                        section.WorkshopPath = string.IsNullOrWhiteSpace(workshop) ? null : workshop;
                    }

                    break;
                case "installpath":
                    if (ReadString(value, path, log, allowNull: true, out var install))
                    {
                        section.InstallPath = string.IsNullOrWhiteSpace(install) ? null : install;
                    }

                    break;
                default:
                    log.Warn($"Config: unknown field '{path}' ignored");
                    break;
            }
        }
    }

    private static bool ReadString(JsonNode? node, string name, ILog log, bool allowNull, out string? value)
    {
        value = null;
        if (node is null)
        {
            if (!allowNull)
            {
                log.Warn($"Config: '{name}' must not be null; using default");
            }

            return allowNull;
        }

        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String)
        {
            value = v.GetValue<string>();
            return true;
        }

        log.Warn($"Config: '{name}' must be a string; using default");
        return false;
    }

    private static bool ReadBool(JsonNode? node, string name, ILog log, out bool value)
    {
        value = false;
        var kind = node?.GetValueKind();
        if (kind is JsonValueKind.True or JsonValueKind.False)
        {
            value = kind == JsonValueKind.True;
            return true;
        }

        log.Warn($"Config: '{name}' must be true or false; using default");
        return false;
    }

    private static bool ReadInt(JsonNode? node, string name, ILog log, out int value)
    {
        value = 0;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<double>(out var number))
        {
            // Out-of-range numbers are pinned to int limits here and clamped properly by the validator.
            value = number >= int.MaxValue ? int.MaxValue
                : number <= int.MinValue ? int.MinValue
                : (int)Math.Round(number);
            return true;
        }

        log.Warn($"Config: '{name}' must be a number; using default");
        return false;
    }

    private static bool ReadEnum<T>(JsonNode? node, string name, ILog log, out T value)
        where T : struct, Enum
    {
        value = default;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String)
        {
            var text = v.GetValue<string>().Trim();
            if (!text.Any(char.IsDigit) && Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value))
            {
                return true;
            }
        }

        var allowed = string.Join(", ", Enum.GetNames<T>().Select(JsonNamingPolicy.CamelCase.ConvertName));
        log.Warn($"Config: '{name}' must be one of {allowed}; using default");
        return false;
    }
}

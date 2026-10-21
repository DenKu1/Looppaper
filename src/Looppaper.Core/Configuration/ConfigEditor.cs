using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Looppaper.Core.Configuration;

/// <summary>
/// Changes single settings in <c>config.json</c> on disk while keeping every other value the user
/// wrote (including fields this version does not know). Comments are not kept. A file that is not
/// valid JSON is left alone rather than overwritten.
/// </summary>
public static class ConfigEditor
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Sets <c>video.enabled = true</c> and <c>video.file</c> to <paramref name="videoFile"/>.</summary>
    /// <returns>Null on success, otherwise why the file was not changed.</returns>
    public static string? SetVideo(string configPath, string videoFile) => Edit(configPath, text => SetVideoInText(text, videoFile));

    /// <summary>Sets <c>trayIcon</c>.</summary>
    /// <returns>Null on success, otherwise why the file was not changed.</returns>
    public static string? SetTrayIcon(string configPath, bool show) => Edit(configPath, text => SetTrayIconInText(text, show));

    /// <summary>Applies <paramref name="change"/> to the config text on disk (the defaults when there is no file yet).</summary>
    private static string? Edit(string configPath, Func<string, string> change)
    {
        string text;
        try
        {
            text = File.Exists(configPath) ? File.ReadAllText(configPath) : ConfigLoader.Serialize(new LooppaperConfig());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"cannot read {configPath}: {ex.Message}";
        }

        string updated;
        try
        {
            updated = change(text);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return $"{configPath} is not valid JSON ({ex.Message}); fix or delete it and try again";
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(configPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Write next to it and swap, so a crash never leaves half a config behind.
            var temp = configPath + ".tmp";
            File.WriteAllText(temp, updated, new UTF8Encoding(false));
            File.Move(temp, configPath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"cannot write {configPath}: {ex.Message}";
        }
    }

    /// <summary>The config text with <c>trayIcon</c> set; throws <see cref="JsonException"/> for invalid JSON.</summary>
    public static string SetTrayIconInText(string configText, bool show)
    {
        var root = ParseRoot(configText);
        root[FindKey(root, "trayIcon") ?? "trayIcon"] = show;
        return Write(root);
    }

    /// <summary>The config text with the video set; throws <see cref="JsonException"/> for invalid JSON.</summary>
    public static string SetVideoInText(string configText, string videoFile)
    {
        var root = ParseRoot(configText);
        var videoKey = FindKey(root, "video") ?? "video";
        if (root[videoKey] is not JsonObject video)
        {
            video = new JsonObject();
            root[videoKey] = video;
        }

        video[FindKey(video, "enabled") ?? "enabled"] = true;
        video[FindKey(video, "file") ?? "file"] = videoFile;
        return Write(root);
    }

    private static JsonObject ParseRoot(string configText) =>
        JsonNode.Parse(configText.TrimStart('﻿'),documentOptions: ReadOptions) as JsonObject
            ?? throw new JsonException("the root is not a JSON object");

    private static string Write(JsonObject root) =>
        ConfigLoader.Header + root.ToJsonString(WriteOptions).Replace("\n", "\r\n", StringComparison.Ordinal).Replace("\r\r\n", "\r\n", StringComparison.Ordinal) + "\r\n";

    /// <summary>The existing spelling of a key (config.json is case-insensitive), or null.</summary>
    private static string? FindKey(JsonObject obj, string name) =>
        obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
}

using System.Text.Json;

namespace Looppaper.Core.WallpaperEngine;

/// <summary>Wallpaper Engine wallpaper kinds, from <c>project.json</c>'s <c>type</c>.</summary>
public enum WorkshopItemType
{
    Video,
    Scene,
    Web,
    Application,
    Unknown,
}

/// <summary>The fields Looppaper uses from a Workshop item's <c>project.json</c>.</summary>
/// <param name="Title">The title, or null when missing.</param>
/// <param name="TypeText">The <c>type</c> as written (case varies between items), or null.</param>
/// <param name="File">The main file relative to the item folder (a video, <c>scene.json</c>, <c>index.html</c>...).</param>
/// <param name="Preview">The preview picture relative to the item folder.</param>
public sealed record ProjectInfo(string? Title, string? TypeText, string? File, string? Preview)
{
    public WorkshopItemType Type => ProjectFile.ParseType(TypeText);
}

/// <summary>Reads <c>project.json</c> leniently: comments, trailing commas, any key case, odd field types.</summary>
public static class ProjectFile
{
    public const string FileName = "project.json";

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses the JSON text; returns null when it is not a JSON object.</summary>
    public static ProjectInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.TrimStart('﻿'), Options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? Text(string name)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var text = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => null,
                    };
                    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                }

                return null;
            }

            return new ProjectInfo(Text("title"), Text("type"), Text("file"), Text("preview"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads <c>project.json</c> in <paramref name="folder"/>; null when it is missing or unreadable.</summary>
    public static ProjectInfo? Read(string folder)
    {
        var path = Path.Combine(folder, FileName);
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static WorkshopItemType ParseType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "video" => WorkshopItemType.Video,
        "scene" => WorkshopItemType.Scene,
        "web" => WorkshopItemType.Web,
        "application" => WorkshopItemType.Application,
        _ => WorkshopItemType.Unknown,
    };
}

using System.Text.Json;

namespace Looppaper.Core.Updates;

/// <summary>The fields of a GitHub release (REST API <c>releases/latest</c>) the update check needs.</summary>
public sealed record GitHubRelease(string TagName, string? Name, string HtmlUrl, bool Prerelease, bool Draft, DateTimeOffset? PublishedAt)
{
    /// <summary>
    /// Parses a release object from the GitHub REST API. Returns false (with a reason) when the JSON is
    /// malformed or lacks <c>tag_name</c> / <c>html_url</c>; unknown fields are ignored.
    /// </summary>
    public static bool TryParse(string json, out GitHubRelease release, out string error)
    {
        release = null!;
        error = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "the release is not a JSON object";
                return false;
            }

            var tag = GetString(root, "tag_name");
            var url = GetString(root, "html_url");
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
            {
                error = "the release has no tag_name or html_url";
                return false;
            }

            DateTimeOffset? published = null;
            if (root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && p.TryGetDateTimeOffset(out var date))
            {
                published = date;
            }

            release = new GitHubRelease(
                tag.Trim(),
                GetString(root, "name"),
                url.Trim(),
                GetBool(root, "prerelease"),
                GetBool(root, "draft"),
                published);
            return true;
        }
        catch (JsonException ex)
        {
            error = "the response is not valid JSON (" + ex.Message + ")";
            return false;
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

// Ported from wp2video (https://github.com/unio207/wp2video), MIT License — see THIRD-PARTY-NOTICES.md
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Looppaper.Core.WallpaperEngine;

/// <summary>How long a wallpaper says it loops, and where that came from.</summary>
public sealed record LoopLength(double Seconds, string Source);

/// <summary>
/// Reads what the scene / web conversion needs from a Workshop item's own files: <c>scene.json</c>
/// (loose or inside <c>scene.pkg</c>), the scene's native size, its loop length, and the user's
/// alignment position from Wallpaper Engine's <c>config.json</c>. Everything is read-only.
/// </summary>
public static partial class SceneFiles
{
    /// <summary>wp2video only trusts loop lengths up to 10 minutes; longer ones are treated as "no loop".</summary>
    public const double MaxLoopSeconds = 600;

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The scene's <c>scene.json</c> text, loose or from <c>scene.pkg</c>; null when there is neither.</summary>
    public static string? ReadSceneJson(string folder)
    {
        try
        {
            var loose = Path.Combine(folder, "scene.json");
            if (File.Exists(loose))
            {
                return File.ReadAllText(loose);
            }

            var pkg = Path.Combine(folder, "scene.pkg");
            if (File.Exists(pkg))
            {
                using var stream = File.OpenRead(pkg);
                return ReadFromPackage(stream, "scene.json");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>
    /// Reads one file from a Wallpaper Engine <c>.pkg</c>: a length-prefixed version string, an entry
    /// count, then (name, offset, size) entries; offsets are relative to the end of the entry table.
    /// Returns null for a damaged package or a missing entry.
    /// </summary>
    public static string? ReadFromPackage(Stream stream, string entryName)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            string? ReadString()
            {
                var length = reader.ReadUInt32();
                if (length > 4096 || stream.Position + length > stream.Length)
                {
                    return null;
                }

                return Encoding.UTF8.GetString(reader.ReadBytes((int)length));
            }

            if (ReadString() is null)
            {
                return null;
            }

            var count = reader.ReadUInt32();
            if (count > 100_000)
            {
                return null;
            }

            var entries = new List<(string Name, uint Offset, uint Size)>();
            for (var i = 0; i < count; i++)
            {
                var name = ReadString();
                if (name is null)
                {
                    return null;
                }

                entries.Add((name, reader.ReadUInt32(), reader.ReadUInt32()));
            }

            var dataStart = stream.Position;
            foreach (var (name, offset, size) in entries)
            {
                if (name != entryName)
                {
                    continue;
                }

                if (dataStart + offset + size > stream.Length)
                {
                    return null;
                }

                stream.Position = dataStart + offset;
                return Encoding.UTF8.GetString(reader.ReadBytes((int)size)).TrimStart('﻿');
            }
        }
        catch (EndOfStreamException)
        {
        }

        return null;
    }

    /// <summary>Native (width, height) from <c>general.orthogonalprojection</c>, or null.</summary>
    public static (int Width, int Height)? SceneSize(string? sceneJson)
    {
        if (sceneJson is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(sceneJson, Options);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("general", out var general) && general.ValueKind == JsonValueKind.Object
                && general.TryGetProperty("orthogonalprojection", out var projection) && projection.ValueKind == JsonValueKind.Object
                && Number(projection, "width") is { } width && Number(projection, "height") is { } height
                && width >= 1 && height >= 1)
            {
                return ((int)width, (int)height);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// A scene loops only if it has timeline animations set to loop: the loop is the least common
    /// multiple of their lengths (<c>length / fps</c>). Particles and time-driven shaders have no period,
    /// so even then they don't repeat exactly. Null when there is no usable loop.
    /// </summary>
    public static LoopLength? SceneLoop(string? sceneJson)
    {
        if (sceneJson is null)
        {
            return null;
        }

        var periods = new List<Rational>();
        try
        {
            using var doc = JsonDocument.Parse(sceneJson, Options);
            Walk(doc.RootElement, periods);
        }
        catch (JsonException)
        {
            return null;
        }

        if (periods.Count == 0)
        {
            return null;
        }

        var loop = periods[0];
        foreach (var period in periods.Skip(1))
        {
            loop = Rational.Lcm(loop, period);
        }

        var seconds = loop.ToDouble();
        return seconds is > 0 and <= MaxLoopSeconds ? new LoopLength(seconds, "its looping timeline animations") : null;
    }

    /// <summary>The loop length of a scene or web item, or null when it does not say.</summary>
    /// <param name="mediaDuration">Returns a video file's length (ffprobe), or null when unknown.</param>
    public static LoopLength? LoopFor(WorkshopItem item, Func<string, TimeSpan?> mediaDuration)
    {
        if (item.Type == WorkshopItemType.Scene)
        {
            return SceneLoop(ReadSceneJson(item.Folder));
        }

        if (item.Type != WorkshopItemType.Web)
        {
            return null;
        }

        try
        {
            var page = item.FilePath ?? Path.Combine(item.Folder, "index.html");
            if (!File.Exists(page) || WebLoopVideo(File.ReadAllText(page)) is not { } src)
            {
                return null;
            }

            var video = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(page) ?? item.Folder, Uri.UnescapeDataString(src).Replace('/', Path.DirectorySeparatorChar)));
            if (!video.StartsWith(Path.GetFullPath(item.Folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(video))
            {
                return null;
            }

            return mediaDuration(video) is { TotalSeconds: > 0 } duration ? new LoopLength(duration.TotalSeconds, Path.GetFileName(video)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// A web page built around one looping <c>&lt;video&gt;</c> loops with that video: returns its
    /// <c>src</c> (relative to the page), or null when there is not exactly one looping video.
    /// </summary>
    public static string? WebLoopVideo(string html)
    {
        var tags = VideoTag().Matches(html);
        if (tags.Count != 1)
        {
            return null;
        }

        var tag = tags[0].Value;
        var opening = tag.Split('>')[0];
        if (!LoopAttribute().IsMatch(opening))
        {
            return null;
        }

        var src = SrcAttribute().Match(tag);
        return src.Success ? src.Groups[1].Value : null;
    }

    /// <summary>
    /// The user's alignment position slider (0-100) for this wallpaper, read from Wallpaper Engine's
    /// <c>config.json</c> (first monitor preferred); 50 (centered) when it is not set.
    /// </summary>
    public static double UserPosition(string? wallpaperEngineConfigJson, string itemFolder)
    {
        if (wallpaperEngineConfigJson is null)
        {
            return 50;
        }

        try
        {
            using var doc = JsonDocument.Parse(wallpaperEngineConfigJson, Options);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return 50;
            }

            var want = NormalizeFolder(itemFolder);
            foreach (var user in doc.RootElement.EnumerateObject())
            {
                if (user.Value.ValueKind != JsonValueKind.Object
                    || !user.Value.TryGetProperty("wproperties", out var properties)
                    || properties.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var file in properties.EnumerateObject())
                {
                    if (NormalizeFolder(Path.GetDirectoryName(file.Name.Replace('/', '\\')) ?? string.Empty) != want
                        || file.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var monitor in file.Value.EnumerateObject().OrderBy(m => m.Name != "MonitorPositionL0T0"))
                    {
                        if (monitor.Value.ValueKind == JsonValueKind.Object && Number(monitor.Value, "alignmentposition") is { } position)
                        {
                            return Math.Clamp(position, 0, 100);
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return 50;
    }

    private static string NormalizeFolder(string folder) =>
        folder.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    private static void Walk(JsonElement element, List<Rational> periods)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("animation", out var animation)
                && animation.ValueKind == JsonValueKind.Object
                && animation.TryGetProperty("options", out var options)
                && options.ValueKind == JsonValueKind.Object
                && options.TryGetProperty("mode", out var mode)
                && mode.ValueKind == JsonValueKind.String && mode.GetString() == "loop"
                && Rational.TryParse(options, "fps", out var fps) && !fps.IsZero
                && Rational.TryParse(options, "length", out var length) && !length.IsZero)
            {
                periods.Add(length / fps);
            }

            foreach (var property in element.EnumerateObject())
            {
                Walk(property.Value, periods);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, periods);
            }
        }
    }

    private static double? Number(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    [GeneratedRegex(@"<video\b[^>]*>(?:.*?</video>)?", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex VideoTag();

    [GeneratedRegex(@"\bloop\b", RegexOptions.IgnoreCase)]
    private static partial Regex LoopAttribute();

    [GeneratedRegex(@"\bsrc\s*=\s*[""']([^""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex SrcAttribute();
}

/// <summary>An exact non-negative fraction, for adding up animation periods without rounding.</summary>
public readonly record struct Rational
{
    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException();
        }

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = gcd.IsZero ? numerator : numerator / gcd;
        Denominator = gcd.IsZero ? denominator : denominator / gcd;
    }

    public BigInteger Numerator { get; }

    public BigInteger Denominator { get; }

    public bool IsZero => Numerator.IsZero;

    public static Rational operator /(Rational a, Rational b) =>
        new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    /// <summary>Least common multiple of two fractions: lcm of the numerators over gcd of the denominators.</summary>
    public static Rational Lcm(Rational a, Rational b)
    {
        var gcd = BigInteger.GreatestCommonDivisor(a.Numerator, b.Numerator);
        var lcm = gcd.IsZero ? BigInteger.Zero : BigInteger.Abs(a.Numerator / gcd * b.Numerator);
        return new Rational(lcm, BigInteger.GreatestCommonDivisor(a.Denominator, b.Denominator));
    }

    /// <summary>Parses a decimal number written in JSON ("30", "12.5", "1e2" or the same as a string) exactly.</summary>
    public static bool TryParse(string text, out Rational value)
    {
        value = default;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d < 0)
        {
            return false;
        }

        var bits = decimal.GetBits(d);
        var scale = (bits[3] >> 16) & 0xFF;
        var mantissa = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | new BigInteger((uint)bits[0]);
        value = new Rational(mantissa, BigInteger.Pow(10, scale));
        return true;
    }

    internal static bool TryParse(JsonElement obj, string name, out Rational value)
    {
        value = default;
        if (!obj.TryGetProperty(name, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number => TryParse(element.GetRawText(), out value),
            JsonValueKind.String => TryParse(element.GetString() ?? string.Empty, out value),
            _ => false,
        };
    }

    public double ToDouble() => (double)Numerator / (double)Denominator;

    public override string ToString() => $"{Numerator}/{Denominator}";
}

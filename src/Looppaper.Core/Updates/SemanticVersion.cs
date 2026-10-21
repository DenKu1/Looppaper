using System.Globalization;
using System.Reflection;

namespace Looppaper.Core.Updates;

/// <summary>
/// A semantic version (<c>major.minor.patch[-prerelease][+build]</c>) compared by SemVer 2.0 precedence:
/// a pre-release sorts before its release, pre-release identifiers compare numerically when both are
/// numbers and ordinally otherwise, and build metadata is ignored.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string[] _prerelease;

    private SemanticVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prerelease = prerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The pre-release part without the dash (e.g. <c>beta.1</c>), or an empty string for a release.</summary>
    public string Prerelease => string.Join('.', _prerelease);

    public bool IsPrerelease => _prerelease.Length > 0;

    /// <summary>
    /// Parses <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2.3-beta.1</c> or <c>1.2.3+abc</c>. A missing patch
    /// (<c>1.2</c>) counts as 0, and a fourth .NET revision part (<c>1.2.3.0</c>) is accepted when it is 0.
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s[0] is 'v' or 'V')
        {
            s = s[1..];
        }

        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            s = s[..plus];
        }

        string[] prerelease = [];
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            prerelease = s[(dash + 1)..].Split('.');
            s = s[..dash];
            if (prerelease.Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
            {
                return false;
            }
        }

        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)
                || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        if (parts.Length == 4 && numbers[3] != 0)
        {
            return false;
        }

        version = new SemanticVersion(numbers[0], numbers[1], parts.Length > 2 ? numbers[2] : 0, prerelease);
        return true;
    }

    /// <summary>
    /// The version an assembly was built as: its informational version (what <c>-p:Version=</c> sets,
    /// including any pre-release suffix), falling back to the assembly version.
    /// </summary>
    public static SemanticVersion FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParse(informational, out var version))
        {
            return version;
        }

        var v = assembly.GetName().Version ?? new Version(0, 0, 0);
        return new SemanticVersion(v.Major, v.Minor, Math.Max(v.Build, 0), []);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        return result != 0 ? result : ComparePrerelease(_prerelease, other._prerelease);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, _prerelease.Length);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}") + (IsPrerelease ? "-" + Prerelease : string.Empty);

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    private static int ComparePrerelease(string[] a, string[] b)
    {
        // A release has higher precedence than any of its pre-releases.
        if (a.Length == 0 || b.Length == 0)
        {
            return b.Length.CompareTo(a.Length);
        }

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = long.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bNumeric = long.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);
            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i]),
            };
            if (result != 0)
            {
                return Math.Sign(result);
            }
        }

        return a.Length.CompareTo(b.Length);
    }
}

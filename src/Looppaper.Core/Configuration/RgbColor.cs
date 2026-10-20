using System.Globalization;

namespace Looppaper.Core.Configuration;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static readonly RgbColor Black = new(0, 0, 0);

    /// <summary>Win32 COLORREF layout (0x00BBGGRR).</summary>
    public uint ToColorRef() => R | ((uint)G << 8) | ((uint)B << 16);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Parses "#RRGGBB" (the leading # is optional).</summary>
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = Black;
        if (text is null)
        {
            return false;
        }

        var s = text.Trim();
        if (s.StartsWith('#'))
        {
            s = s[1..];
        }

        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = new RgbColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }
}

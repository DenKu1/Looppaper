using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Looppaper.Core.Rendering;

namespace Looppaper.Rendering;

/// <summary>
/// Looppaper's own wallpaper, drawn when there is nothing in the collection to show: a dark blue gradient with a
/// few soft rings, the name, and where to put pictures and videos. Drawn at each monitor's size, so it is sharp
/// at any resolution, and nothing is written to disk.
/// </summary>
internal static class DefaultWallpaper
{
    private static readonly Color Top = Color.FromArgb(0x0B, 0x16, 0x2C);
    private static readonly Color Bottom = Color.FromArgb(0x16, 0x3E, 0x5C);
    private static readonly Color Ring = Color.FromArgb(0x5E, 0xC8, 0xD8);

    /// <summary>Draws the wallpaper into every monitor's rectangle of <paramref name="g"/>.</summary>
    /// <param name="hint">Lines under the name, e.g. the collection folders; may be empty.</param>
    public static void Draw(Graphics g, IReadOnlyList<IntRect> monitors, IReadOnlyList<string> hint)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Width <= 0 || monitor.Height <= 0)
            {
                continue;
            }

            var area = new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height);
            g.SetClip(area);
            DrawMonitor(g, area, hint);
        }

        g.ResetClip();
    }

    private static void DrawMonitor(Graphics g, Rectangle area, IReadOnlyList<string> hint)
    {
        using (var gradient = new LinearGradientBrush(area, Top, Bottom, LinearGradientMode.ForwardDiagonal))
        {
            g.FillRectangle(gradient, area);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var unit = Math.Min(area.Width, area.Height);

        // Overlapping rings, the "loop": centred a little right of and below the middle.
        var cx = area.X + area.Width * 0.62f;
        var cy = area.Y + area.Height * 0.58f;
        for (var i = 0; i < 5; i++)
        {
            var radius = unit * (0.22f + i * 0.11f);
            var alpha = 46 - i * 8;
            using var pen = new Pen(Color.FromArgb(alpha, Ring), Math.Max(1f, unit / 360f));
            g.DrawEllipse(pen, cx - radius - i * unit * 0.03f, cy - radius, radius * 2, radius * 2);
        }

        using (var glow = new GraphicsPath())
        {
            var r = unit * 0.5f;
            glow.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            using var brush = new PathGradientBrush(glow)
            {
                CenterColor = Color.FromArgb(38, Ring),
                SurroundColors = [Color.FromArgb(0, Ring)],
            };
            g.FillPath(brush, glow);
        }

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var left = area.X + area.Width * 0.08f;
        var titleSize = Math.Max(12f, unit * 0.075f);
        using var title = new Font("Segoe UI Light", titleSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
        var titleTop = area.Y + area.Height * 0.36f;
        g.DrawString("Looppaper", title, titleBrush, left, titleTop);

        if (hint.Count == 0)
        {
            return;
        }

        var hintSize = Math.Max(10f, unit * 0.018f);
        using var small = new Font("Segoe UI", hintSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var hintBrush = new SolidBrush(Color.FromArgb(170, 214, 232, 240));
        var y = titleTop + titleSize * 1.45f;
        foreach (var line in hint)
        {
            g.DrawString(line, small, hintBrush, left + titleSize * 0.06f, y);
            y += hintSize * 1.6f;
        }
    }
}

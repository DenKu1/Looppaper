using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Looppaper.Core.Configuration;
using Looppaper.Core.Logging;
using Looppaper.Core.Rendering;

namespace Looppaper.Rendering;

/// <summary>
/// Turns a picture file into a ready-to-blit GDI bitmap covering the whole wallpaper window:
/// decoded once, scaled once per monitor, and the decoded original released straight away.
/// Safe to call from a background thread.
/// </summary>
internal static class PictureRenderer
{
    /// <summary>Pictures above this many pixels are refused rather than decoded (~1.6 GB of RAM).</summary>
    private const long MaxPixels = 400L * 1000 * 1000;

    private const int ExifOrientationId = 0x0112;

    /// <summary>Renders <paramref name="path"/> (or only the background when null). Returns an HBITMAP or null on failure.</summary>
    public static IntPtr Render(string? path, int width, int height, IReadOnlyList<IntRect> monitors, ScalingMode scaling, RgbColor background, ILog log)
    {
        if (width <= 0 || height <= 0)
        {
            return IntPtr.Zero;
        }

        Image? picture = null;
        if (path is not null)
        {
            picture = TryDecode(path, log);
            if (picture is null)
            {
                return IntPtr.Zero;
            }
        }

        try
        {
            using var canvas = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.FromArgb(background.R, background.G, background.B));
                if (picture is not null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.None;
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    using var attributes = new ImageAttributes();
                    attributes.SetWrapMode(WrapMode.TileFlipXY); // avoids a faint border from bicubic edge sampling

                    foreach (var monitor in monitors)
                    {
                        var dest = ScalingMath.Destination(picture.Width, picture.Height, monitor, scaling);
                        g.SetClip(new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height));
                        g.DrawImage(
                            picture,
                            new Rectangle(dest.X, dest.Y, dest.Width, dest.Height),
                            0,
                            0,
                            picture.Width,
                            picture.Height,
                            GraphicsUnit.Pixel,
                            attributes);
                    }
                }
            }

            // Release the decoded original before allocating the GDI copy to keep the peak low.
            picture?.Dispose();
            picture = null;
            return canvas.GetHbitmap();
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            log.Error($"Rendering {path ?? "background"} failed", ex);
            return IntPtr.Zero;
        }
        finally
        {
            picture?.Dispose();
        }
    }

    private static Image? TryDecode(string path, ILog log)
    {
        try
        {
            // Read the whole file first so the file is not kept open or locked by GDI+.
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes, writable: false);
            using var header = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            if ((long)header.Width * header.Height > MaxPixels)
            {
                log.Warn($"Skipping {path}: {header.Width}x{header.Height} is too large to decode");
                return null;
            }

            var bitmap = new Bitmap(header);
            ApplyExifOrientation(header, bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            log.Warn($"Cannot decode {path}: {ex.Message}");
            return null;
        }
    }

    private static void ApplyExifOrientation(Image source, Image target)
    {
        if (!source.PropertyIdList.Contains(ExifOrientationId))
        {
            return;
        }

        var value = source.GetPropertyItem(ExifOrientationId)?.Value;
        if (value is not { Length: > 0 })
        {
            return;
        }

        var flip = value[0] switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };
        if (flip != RotateFlipType.RotateNoneFlipNone)
        {
            target.RotateFlip(flip);
        }
    }
}

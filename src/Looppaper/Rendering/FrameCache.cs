using Looppaper.Native;

namespace Looppaper.Rendering;

/// <summary>
/// Holds the single pre-rendered frame selected into a memory DC. Painting is one BitBlt.
/// Use from the UI thread only.
/// </summary>
internal sealed class FrameCache : IDisposable
{
    private IntPtr _memoryDc;
    private IntPtr _bitmap;
    private IntPtr _originalBitmap;

    public bool HasFrame => _bitmap != IntPtr.Zero;

    /// <summary>Takes ownership of <paramref name="hbitmap"/> and frees the previous frame.</summary>
    public void Swap(IntPtr hbitmap)
    {
        if (_memoryDc == IntPtr.Zero)
        {
            _memoryDc = Gdi32.CreateCompatibleDC(IntPtr.Zero);
        }

        var previous = Gdi32.SelectObject(_memoryDc, hbitmap);
        if (_bitmap == IntPtr.Zero)
        {
            _originalBitmap = previous;
        }
        else
        {
            Gdi32.DeleteObject(_bitmap);
        }

        _bitmap = hbitmap;
    }

    public void Paint(IntPtr hdc, in RECT area)
    {
        if (_bitmap != IntPtr.Zero)
        {
            Gdi32.BitBlt(hdc, area.Left, area.Top, area.Width, area.Height, _memoryDc, area.Left, area.Top, Gdi32.SRCCOPY);
        }
    }

    public void Dispose()
    {
        if (_memoryDc != IntPtr.Zero)
        {
            if (_originalBitmap != IntPtr.Zero)
            {
                Gdi32.SelectObject(_memoryDc, _originalBitmap);
            }

            Gdi32.DeleteDC(_memoryDc);
            _memoryDc = IntPtr.Zero;
        }

        if (_bitmap != IntPtr.Zero)
        {
            Gdi32.DeleteObject(_bitmap);
            _bitmap = IntPtr.Zero;
        }
    }
}

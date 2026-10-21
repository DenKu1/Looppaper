using Looppaper.Core.Rendering;
using Looppaper.Native;
using System.Runtime.InteropServices;

namespace Looppaper.Desktop;

internal static unsafe partial class Monitors
{
    private static List<RECT>? _collecting;

    /// <summary>
    /// Monitor rectangles relative to the top-left of the virtual screen, which is also the
    /// wallpaper window's client coordinate space.
    /// </summary>
    public static IReadOnlyList<IntRect> RelativeToVirtualScreen()
    {
        var origin = DesktopHost.VirtualScreen();
        _collecting = [];
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &Collect, IntPtr.Zero);
            var result = _collecting
                .Select(r => new IntRect(r.Left - origin.Left, r.Top - origin.Top, r.Width, r.Height))
                .OrderBy(r => r.X)
                .ThenBy(r => r.Y)
                .ToList();
            if (result.Count == 0)
            {
                result.Add(new IntRect(0, 0, origin.Width, origin.Height));
            }

            return result;
        }
        finally
        {
            _collecting = null;
        }
    }

    /// <summary>Size in pixels of the largest monitor (by area).</summary>
    public static (int Width, int Height) Largest()
    {
        var largest = RelativeToVirtualScreen().MaxBy(r => (long)r.Width * r.Height);
        return (largest.Width, largest.Height);
    }

    [UnmanagedCallersOnly]
    private static int Collect(IntPtr monitor, IntPtr hdc, RECT* rect, IntPtr data)
    {
        _collecting?.Add(*rect);
        return 1;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, delegate* unmanaged<IntPtr, IntPtr, RECT*, IntPtr, int> lpfnEnum, IntPtr dwData);
}

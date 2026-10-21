using System.Runtime.InteropServices;

namespace Looppaper.Native;

/// <summary>Window functions used to place Wallpaper Engine's preview window for recording.</summary>
internal static unsafe partial class User32
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle);
}

/// <summary>Keeps the PC and display awake while a wallpaper is being recorded.</summary>
internal static partial class Kernel32
{
    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [LibraryImport("kernel32.dll")]
    public static partial uint SetThreadExecutionState(uint esFlags);
}

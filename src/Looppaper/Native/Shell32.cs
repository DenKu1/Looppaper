using System.Runtime.InteropServices;

namespace Looppaper.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uTimeoutOrVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;
}

/// <summary>Notification-area (tray) icons.</summary>
internal static unsafe partial class Shell32
{
    public const uint NIM_ADD = 0x0;
    public const uint NIM_MODIFY = 0x1;
    public const uint NIM_DELETE = 0x2;
    public const uint NIF_MESSAGE = 0x1;
    public const uint NIF_ICON = 0x2;
    public const uint NIF_TIP = 0x4;
    public const uint NIF_INFO = 0x10;
    public const uint NIIF_INFO = 0x1;
    public const uint NIIF_WARNING = 0x2;
    public const int IDI_APPLICATION = 32512;

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATAW data);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    public static partial IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    /// <summary>Copies <paramref name="text"/> into a fixed NUL-terminated buffer, ending with "…" when it is cut.</summary>
    public static void CopyText(string text, char* target, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        for (var i = 0; i < length; i++)
        {
            target[i] = text[i];
        }

        if (text.Length >= capacity && length > 0)
        {
            target[length - 1] = '…';
        }

        target[length] = '\0';
    }
}

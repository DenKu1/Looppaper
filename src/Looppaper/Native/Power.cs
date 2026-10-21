using System.Runtime.InteropServices;

namespace Looppaper.Native;

/// <summary>Session (lock / disconnect) and power (sleep, display, energy saver, battery) notifications.</summary>
internal static partial class Power
{
    public const uint WM_WTSSESSION_CHANGE = 0x02B1;
    public const int WTS_CONSOLE_CONNECT = 0x1;
    public const int WTS_CONSOLE_DISCONNECT = 0x2;
    public const int WTS_REMOTE_CONNECT = 0x3;
    public const int WTS_REMOTE_DISCONNECT = 0x4;
    public const int WTS_SESSION_LOCK = 0x7;
    public const int WTS_SESSION_UNLOCK = 0x8;
    public const uint NOTIFY_FOR_THIS_SESSION = 0;

    public const uint WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMSUSPEND = 0x4;
    public const int PBT_APMRESUMESUSPEND = 0x7;
    public const int PBT_APMRESUMEAUTOMATIC = 0x12;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;
    public const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    /// <summary>DWORD: 0 = display off, 1 = on, 2 = dimmed.</summary>
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    /// <summary>DWORD: 1 while battery saver is on (Windows 10 and Windows 11 before Energy Saver).</summary>
    public static readonly Guid GUID_POWER_SAVING_STATUS = new("E00958C0-C213-4ACE-AC77-FECCED2EEEA5");

    /// <summary>DWORD: Windows 11 Energy Saver level; 0 = off.</summary>
    public static readonly Guid GUID_ENERGY_SAVER_STATUS = new("550E8400-E29B-41D4-A716-446655440000");

    /// <summary>DWORD: 0 = AC power, 1 = battery, 2 = short-term source such as a UPS.</summary>
    public static readonly Guid GUID_ACDC_POWER_SOURCE = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");

    [StructLayout(LayoutKind.Sequential)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data; // every setting used here is a DWORD
    }

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, in Guid powerSettingGuid, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterPowerSettingNotification(IntPtr handle);
}

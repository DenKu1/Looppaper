using System.Runtime.InteropServices;

namespace Looppaper.Native;

internal static unsafe partial class WinMm
{
    public const uint MM_MCINOTIFY = 0x03B9;
    public const int MCI_NOTIFY_SUCCESSFUL = 0x0001;
    public const int MCI_NOTIFY_SUPERSEDED = 0x0002;
    public const int MCI_NOTIFY_ABORTED = 0x0004;
    public const int MCI_NOTIFY_FAILURE = 0x0008;

    [LibraryImport("winmm.dll", EntryPoint = "mciSendStringW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MciSendString(string command, char* returnString, int returnLength, IntPtr hwndCallback);

    [LibraryImport("winmm.dll", EntryPoint = "mciGetErrorStringW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MciGetErrorString(int error, char* buffer, int length);

    /// <summary>Sends an MCI command; returns null on success or the error text.</summary>
    public static string? Send(string command, IntPtr callback = default)
    {
        var error = MciSendString(command, null, 0, callback);
        if (error == 0)
        {
            return null;
        }

        var buffer = stackalloc char[256];
        return MciGetErrorString(error, buffer, 256) ? $"{new string(buffer)} (MCI {error})" : $"MCI error {error}";
    }
}

using System.Runtime.InteropServices;

namespace Looppaper.Native;

internal static unsafe partial class Kernel32
{
    public const int STD_OUTPUT_HANDLE = -11;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", EntryPoint = "WriteConsoleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteConsole(IntPtr hConsoleOutput, char* lpBuffer, uint nNumberOfCharsToWrite, out uint lpNumberOfCharsWritten, IntPtr lpReserved);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeConsole();

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    public static partial uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? lpModuleName);
}

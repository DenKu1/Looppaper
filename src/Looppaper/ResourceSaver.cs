using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Looppaper.Core.Logging;

namespace Looppaper;

/// <summary>Keeps Looppaper out of the way: low priority, EcoQoS, and a small working set.</summary>
internal static unsafe partial class ResourceSaver
{
    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

    public static void Apply(ILog log)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Warn($"Could not lower process priority: {ex.Message}");
        }

        // EcoQoS (Windows 11; Windows 10 1709+ honours the older "throttled" form): run on efficient
        // cores at low clock and do not let us raise the system timer resolution.
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
            StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
        };
        try
        {
            if (!SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE)))
            {
                log.Info($"EcoQoS not available ({Marshal.GetLastPInvokeError()})");
            }
        }
        catch (EntryPointNotFoundException)
        {
            log.Info("EcoQoS not available on this Windows version");
        }
    }

    /// <summary>Releases memory left over from decoding: compacts the GC heap and trims the working set.</summary>
    public static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr hProcess, int processInformationClass, void* processInformation, uint processInformationSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(IntPtr hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);
}

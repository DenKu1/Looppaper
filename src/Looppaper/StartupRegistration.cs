using Microsoft.Win32;

namespace Looppaper;

/// <summary>Per-user "start when I sign in" entry under HKCU\...\Run (no admin rights needed).</summary>
internal static class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Looppaper";

    public static string Command(string exePath) => $"\"{exePath}\"";

    public static void Install(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, Command(exePath), RegistryValueKind.String);
    }

    /// <summary>Returns false when there was nothing to remove.</summary>
    public static bool Uninstall()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is null)
        {
            return false;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        return true;
    }

    public static string? Current()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }
}

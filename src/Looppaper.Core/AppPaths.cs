namespace Looppaper.Core;

/// <summary>Well-known per-user locations.</summary>
public static class AppPaths
{
    public const string AppName = "Looppaper";

    public static string DefaultConfigFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "config.json");

    public static string LogFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "logs", "looppaper.log");

    /// <summary>Converted and compressed videos (Epics 8 and 9). Safe to delete; it is rebuilt on demand.</summary>
    public static string CacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "cache");

    /// <summary>Expands environment variables (e.g. <c>%USERPROFILE%</c>), strips quotes and makes the path absolute.</summary>
    public static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetFullPath(expanded);
    }
}

namespace Looppaper.Core;

public enum InstallLayout
{
    /// <summary>Everything (config, collection, cache, logs) lives in the folder next to Looppaper.exe.</summary>
    Portable,

    /// <summary>Windows-style split: config in %APPDATA%, cache and logs in %LOCALAPPDATA%, collection in Pictures.</summary>
    Standard,
}

/// <summary>
/// Where Looppaper keeps its files. A <see cref="PortableMarker"/> file next to the exe selects the
/// <see cref="InstallLayout.Portable"/> layout (the installer's default); without it the
/// <see cref="InstallLayout.Standard"/> per-user folders are used.
/// </summary>
public static class AppPaths
{
    public const string AppName = "Looppaper";

    /// <summary>Empty file next to Looppaper.exe that switches to the portable, all-in-one-folder layout.</summary>
    public const string PortableMarker = "looppaper.portable";

    /// <summary>Collection folder name inside the install folder (portable layout).</summary>
    public const string PortableCollectionFolder = "collection";

    private static string? _portableRoot;

    public static InstallLayout Layout => _portableRoot is null ? InstallLayout.Standard : InstallLayout.Portable;

    /// <summary>The install folder in the portable layout; null in the standard layout.</summary>
    public static string? PortableRoot => _portableRoot;

    /// <summary>Folder that relative paths in config.json are resolved against.</summary>
    public static string DataRoot => _portableRoot ?? AppContext.BaseDirectory;

    public static string DefaultConfigFile => Path.Combine(_portableRoot ?? Standard.ConfigDirectory, "config.json");

    public static string LogFile => Path.Combine(_portableRoot ?? Standard.LocalDirectory, "logs", "looppaper.log");

    /// <summary>Converted and compressed videos. Safe to delete; it is rebuilt on demand.</summary>
    public static string CacheDirectory => Path.Combine(_portableRoot ?? Standard.LocalDirectory, "cache");

    /// <summary>Which videos have compressed copies (and where).</summary>
    public static string CompressionStateFile => Path.Combine(_portableRoot ?? Standard.LocalDirectory, "compression.json");

    /// <summary>Picks the layout from the folder Looppaper.exe runs from (marker file present = portable).</summary>
    public static InstallLayout Configure(string? exeDirectory)
    {
        _portableRoot = exeDirectory is not null && File.Exists(Path.Combine(exeDirectory, PortableMarker))
            ? Path.GetFullPath(exeDirectory)
            : null;
        return Layout;
    }

    /// <summary>For tests: force a layout. A null root selects the standard layout.</summary>
    public static void UsePortableRoot(string? root) => _portableRoot = root is null ? null : Path.GetFullPath(root);

    /// <summary>Expands environment variables (e.g. <c>%USERPROFILE%</c>), strips quotes and makes the path absolute (relative to the current directory).</summary>
    public static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetFullPath(expanded);
    }

    /// <summary>Like <see cref="ExpandPath"/>, but relative paths are relative to <see cref="DataRoot"/> (used for paths inside config.json).</summary>
    public static string ResolveDataPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetFullPath(expanded, DataRoot);
    }

    /// <summary>The standard layout's folders, also used to migrate into the portable layout.</summary>
    public static class Standard
    {
        public const string CollectionFolder = @"%USERPROFILE%\Pictures\Looppaper";

        public static string ConfigDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

        public static string LocalDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

        public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

        public static string CacheDirectory => Path.Combine(LocalDirectory, "cache");

        public static string CompressionStateFile => Path.Combine(LocalDirectory, "compression.json");
    }
}

using Looppaper.Core.Logging;
using Looppaper.Native;

namespace Looppaper;

/// <summary>
/// Posts a message to a window whenever a matching file in one folder is created, changed, renamed or deleted.
/// File system events arrive in bursts on thread-pool threads; the window debounces them on the UI thread.
/// </summary>
internal sealed class FileChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Func<string, bool> _matches;
    private readonly IntPtr _hwnd;
    private readonly uint _message;

    private FileChangeWatcher(FileSystemWatcher watcher, Func<string, bool> matches, IntPtr hwnd, uint message)
    {
        _watcher = watcher;
        _matches = matches;
        _hwnd = hwnd;
        _message = message;
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.Error += (_, _) => Post(); // buffer overflow: something changed, look again
        watcher.EnableRaisingEvents = true;
    }

    /// <summary>Starts watching, or returns null (logged) when the folder does not exist or cannot be watched.</summary>
    public static FileChangeWatcher? Start(string folder, string filter, Func<string, bool> matches, IntPtr hwnd, uint message, ILog log)
    {
        if (!Directory.Exists(folder))
        {
            log.Info($"Not watching {folder}: it does not exist");
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(folder, filter)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 16 * 1024,
            };
            return new FileChangeWatcher(watcher, matches, hwnd, message);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            log.Warn($"Cannot watch {folder} for changes: {ex.Message}");
            return null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (_matches(e.FullPath))
        {
            Post();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (_matches(e.FullPath) || _matches(e.OldFullPath))
        {
            Post();
        }
    }

    private void Post() => User32.PostMessage(_hwnd, _message, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}

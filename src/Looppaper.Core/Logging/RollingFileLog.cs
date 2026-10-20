using System.Globalization;
using System.Text;

namespace Looppaper.Core.Logging;

/// <summary>
/// Appends lines to a log file and rolls it to "&lt;name&gt;.1" once it would exceed <see cref="MaxBytes"/>.
/// Only one backup is kept. The file is opened per write so several processes (for example a
/// <c>--stop</c> invocation) can share it and nothing stays locked between writes.
/// </summary>
public sealed class RollingFileLog : ILog
{
    public const long DefaultMaxBytes = 1024 * 1024;

    private readonly object _gate = new();

    public RollingFileLog(string path, long maxBytes = DefaultMaxBytes, LogLevel minimumLevel = LogLevel.Info)
    {
        FilePath = path;
        MaxBytes = maxBytes;
        MinimumLevel = minimumLevel;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".1";

    public long MaxBytes { get; }

    public LogLevel MinimumLevel { get; set; }

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(LevelTag(level)).Append("] ")
            .Append(message);
        if (exception is not null)
        {
            line.Append(Environment.NewLine).Append("    ")
                .Append(exception.ToString().Replace(Environment.NewLine, Environment.NewLine + "    ", StringComparison.Ordinal));
        }

        line.Append(Environment.NewLine);
        var bytes = Encoding.UTF8.GetBytes(line.ToString());

        lock (_gate)
        {
            try
            {
                RollIfNeeded(bytes.Length);
                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (IOException)
            {
                // Logging must never take the wallpaper down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void RollIfNeeded(int incoming)
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length + incoming <= MaxBytes)
        {
            return;
        }

        File.Delete(BackupPath);
        File.Move(FilePath, BackupPath);
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "???",
    };
}

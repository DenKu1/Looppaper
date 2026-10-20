namespace Looppaper.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Minimal logging abstraction so the core stays free of logging frameworks.</summary>
public interface ILog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}

public static class LogExtensions
{
    public static void Debug(this ILog log, string message) => log.Write(LogLevel.Debug, message);

    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warn(this ILog log, string message, Exception? exception = null) => log.Write(LogLevel.Warning, message, exception);

    public static void Error(this ILog log, string message, Exception? exception = null) => log.Write(LogLevel.Error, message, exception);
}

public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();

    private NullLog()
    {
    }

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
    }
}

using Looppaper.Core.Logging;

namespace Looppaper.Core.Tests;

internal sealed class MemoryLog : ILog
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IEnumerable<string> Warnings => Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    public IEnumerable<string> Errors => Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message);

    public void Write(LogLevel level, string message, Exception? exception = null) => Entries.Add((level, message));
}

/// <summary>A temporary directory deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "looppaper-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name, long size = 16)
    {
        var full = System.IO.Path.Combine(Path, name);
        using var stream = new FileStream(full, FileMode.Create);
        stream.SetLength(size);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

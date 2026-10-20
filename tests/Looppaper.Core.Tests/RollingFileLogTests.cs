using Looppaper.Core.Logging;

namespace Looppaper.Core.Tests;

public class RollingFileLogTests
{
    [Fact]
    public void WritesFormattedLines()
    {
        using var dir = new TempDir();
        var log = new RollingFileLog(Path.Combine(dir.Path, "logs", "app.log"));

        log.Info("hello");
        log.Warn("careful", new InvalidOperationException("boom"));
        log.Debug("hidden");

        var text = File.ReadAllText(log.FilePath);
        Assert.Contains("[INF] hello", text);
        Assert.Contains("[WRN] careful", text);
        Assert.Contains("boom", text);
        Assert.DoesNotContain("hidden", text);
    }

    [Fact]
    public void RollsToSingleBackup()
    {
        using var dir = new TempDir();
        var log = new RollingFileLog(Path.Combine(dir.Path, "app.log"), maxBytes: 200);
        var line = new string('x', 80);

        for (var i = 0; i < 20; i++)
        {
            log.Info($"{i:D2} {line}");
        }

        Assert.True(File.Exists(log.BackupPath));
        Assert.True(new FileInfo(log.FilePath).Length <= 200);
        Assert.True(new FileInfo(log.BackupPath).Length <= 200);
        Assert.Contains("19 ", File.ReadAllText(log.FilePath));
        Assert.Equal(2, Directory.GetFiles(dir.Path).Length);
    }
}

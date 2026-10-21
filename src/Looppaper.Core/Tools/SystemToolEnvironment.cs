using System.Diagnostics;

namespace Looppaper.Core.Tools;

/// <summary>The real machine for <see cref="FfmpegLocator"/>.</summary>
public sealed class SystemToolEnvironment : IToolEnvironment
{
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
    {
        try
        {
            return Directory.EnumerateDirectories(path, searchPattern).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string? ReadVersionLine(string exe)
    {
        try
        {
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-version");
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var first = process.StandardOutput.ReadLine();
            process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return null;
            }

            return string.IsNullOrWhiteSpace(first) ? null : first.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}

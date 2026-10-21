using System.Diagnostics;
using System.Text;

namespace Looppaper.Core.Tools;

/// <summary>Output of a short ffmpeg run.</summary>
public sealed record FfmpegOutput(int ExitCode, byte[] StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Runs ffmpeg once with exactly the given arguments and collects stdout (as bytes) and stderr, with
/// a timeout. For quick probes; long encodes with progress go through <see cref="FfmpegRunner"/>.
/// </summary>
public static class FfmpegProcess
{
    public static FfmpegOutput Run(string exe, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var arg in arguments)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return new FfmpegOutput(-1, [], string.Empty, false);
            }

            process.StandardInput.Close();
            var stdout = Task.Run(() =>
            {
                using var buffer = new MemoryStream();
                process.StandardOutput.BaseStream.CopyTo(buffer);
                return buffer.ToArray();
            });
            var stderr = Task.Run(() => process.StandardError.ReadToEnd());
            if (!process.WaitForExit(timeout))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                process.WaitForExit(5000);
                return new FfmpegOutput(-1, [], SafeResult(stderr) ?? string.Empty, true);
            }

            process.WaitForExit();
            return new FfmpegOutput(process.ExitCode, stdout.Result, stderr.Result, false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new FfmpegOutput(-1, [], ex.Message, false);
        }
    }

    private static string? SafeResult(Task<string> task) =>
        task.Wait(TimeSpan.FromSeconds(2)) ? task.Result : null;

    /// <summary>The last characters of stderr, for messages and the log.</summary>
    public static string Tail(string stderr, int characters = 600)
    {
        var text = stderr.Trim();
        return text.Length <= characters ? text : new StringBuilder("...").Append(text, text.Length - characters, characters).ToString();
    }
}

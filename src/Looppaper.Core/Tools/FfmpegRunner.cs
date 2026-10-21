using System.Diagnostics;
using System.Globalization;

namespace Looppaper.Core.Tools;

/// <summary>A progress snapshot from <c>ffmpeg -progress</c>.</summary>
public readonly record struct FfmpegProgress(TimeSpan Position, double? Speed, double? Fraction, bool Finished)
{
    public override string ToString() =>
        Fraction is { } f
            ? string.Create(CultureInfo.InvariantCulture, $"{f * 100:F0}% ({Position:hh\\:mm\\:ss}{(Speed is { } s ? $", {s:F1}x" : string.Empty)})")
            : string.Create(CultureInfo.InvariantCulture, $"{Position:hh\\:mm\\:ss}");
}

public sealed record FfmpegResult(int ExitCode, IReadOnlyList<string> ErrorTail, bool Cancelled)
{
    public bool Succeeded => ExitCode == 0 && !Cancelled;

    /// <summary>The last few lines ffmpeg wrote to stderr, for the log.</summary>
    public string ErrorSummary => string.Join(" | ", ErrorTail.TakeLast(5));
}

/// <summary>
/// Turns the key=value lines of <c>-progress pipe:1</c> into <see cref="FfmpegProgress"/> snapshots.
/// ffmpeg ends each block with a <c>progress=continue</c> or <c>progress=end</c> line.
/// </summary>
public sealed class FfmpegProgressParser
{
    private readonly TimeSpan? _total;
    private TimeSpan _position;
    private double? _speed;

    public FfmpegProgressParser(TimeSpan? totalDuration)
    {
        _total = totalDuration is { Ticks: > 0 } ? totalDuration : null;
    }

    /// <summary>Feeds one line; returns a snapshot when the line completes a block.</summary>
    public FfmpegProgress? Feed(string line)
    {
        var eq = line.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0)
        {
            return null;
        }

        var key = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        switch (key)
        {
            case "out_time_us":
            case "out_time_ms": // despite the name, also microseconds
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0)
                {
                    _position = TimeSpan.FromTicks(us * 10);
                }

                return null;
            case "speed":
                _speed = double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ? speed : null;
                return null;
            case "progress":
                var finished = value == "end";
                double? fraction = _total is { } total
                    ? Math.Clamp(finished ? 1.0 : _position.TotalSeconds / total.TotalSeconds, 0, 1)
                    : null;
                return new FfmpegProgress(_position, _speed, fraction, finished);
            default:
                return null;
        }
    }
}

/// <summary>Runs ffmpeg with progress reporting. Arguments are passed as a list, never as one string.</summary>
public static class FfmpegRunner
{
    private const int ErrorTailLines = 40;

    /// <param name="totalDuration">Length of the output, used to turn progress into a fraction.</param>
    public static async Task<FfmpegResult> RunAsync(
        FfmpegTool tool,
        IReadOnlyList<string> arguments,
        TimeSpan? totalDuration = null,
        IProgress<FfmpegProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(tool.FfmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-nostats", "-progress", "pipe:1" })
        {
            start.ArgumentList.Add(arg);
        }

        foreach (var arg in arguments)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {tool.FfmpegPath}");
        var parser = new FfmpegProgressParser(totalDuration);
        var tail = new Queue<string>();

        var stdout = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                if (parser.Feed(line) is { } snapshot)
                {
                    progress?.Report(snapshot);
                }
            }
        }, CancellationToken.None);
        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                lock (tail)
                {
                    tail.Enqueue(line);
                    if (tail.Count > ErrorTailLines)
                    {
                        tail.Dequeue();
                    }
                }
            }
        }, CancellationToken.None);

        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            try
            {
                // Ask ffmpeg to finish cleanly first ("q"), then force it.
                await process.StandardInput.WriteAsync('q').ConfigureAwait(false);
                await process.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }

            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
            }
        }

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        lock (tail)
        {
            return new FfmpegResult(process.ExitCode, tail.ToList(), cancelled);
        }
    }
}

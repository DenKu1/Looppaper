using System.Net;
using Looppaper.Core.Logging;
using Looppaper.Core.Updates;

namespace Looppaper;

/// <summary>
/// Runs <see cref="UpdateChecker"/> against GitHub: once on demand (<c>--check-update</c>), or once in
/// the background when the wallpaper starts with <c>"checkForUpdates": true</c>. Only reports; never
/// downloads or installs anything.
/// </summary>
internal static class UpdateCheck
{
    /// <summary>The version this exe was built as (the release tag's version in published builds).</summary>
    public static SemanticVersion CurrentVersion => SemanticVersion.FromAssembly(typeof(UpdateCheck).Assembly);

    /// <summary>Checks once, waiting for the answer (at most the default timeout).</summary>
    public static UpdateCheckResult Run(ILog log)
    {
        var result = CheckAsync().GetAwaiter().GetResult();
        Log(log, result);
        return result;
    }

    /// <summary>
    /// Checks once on a thread-pool thread and logs the result. Returns immediately; the wallpaper is
    /// never blocked, and any failure is only logged.
    /// </summary>
    public static void StartInBackground(ILog log)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                Log(log, await CheckAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                log.Warn("Update check failed", ex);
            }
        });
    }

    private static async Task<UpdateCheckResult> CheckAsync()
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AutomaticDecompression = DecompressionMethods.All,
        };
        using var client = new HttpClient(handler, disposeHandler: false);
        return await UpdateChecker.CheckAsync(client, CurrentVersion, UpdateChecker.DefaultTimeout).ConfigureAwait(false);
    }

    private static void Log(ILog log, UpdateCheckResult result)
    {
        if (result.Status == UpdateStatus.Failed)
        {
            log.Warn(result.Message);
        }
        else
        {
            log.Info(result.Message);
        }
    }
}

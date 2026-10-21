using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Looppaper.Core.Updates;

public enum UpdateStatus
{
    /// <summary>The latest release is newer than the running version.</summary>
    UpdateAvailable,

    /// <summary>The running version is the latest release, or newer (a development or pre-release build).</summary>
    UpToDate,

    /// <summary>GitHub answered 404: nothing released yet, or the repository is private (invisible without a token).</summary>
    NoPublicReleases,

    /// <summary>The check could not be completed (network, timeout, rate limit, unexpected response).</summary>
    Failed,
}

/// <summary>Outcome of one update check. <see cref="Message"/> is a complete sentence for the log or console.</summary>
public sealed record UpdateCheckResult(UpdateStatus Status, SemanticVersion Current, SemanticVersion? Latest, string? ReleaseUrl, string Message);

/// <summary>
/// Asks GitHub for the latest Looppaper release and compares it with the running version. It only
/// reports: nothing is ever downloaded or installed. Never throws; every failure becomes a
/// <see cref="UpdateStatus.Failed"/> result.
/// </summary>
public static class UpdateChecker
{
    /// <summary>GitHub REST API endpoint for the latest published (non-draft, non-pre-release) release.</summary>
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/DenKu1/Looppaper/releases/latest";

    public const string ReleasesPageUrl = "https://github.com/DenKu1/Looppaper/releases";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Queries <see cref="LatestReleaseApiUrl"/> once and evaluates the answer.</summary>
    public static async Task<UpdateCheckResult> CheckAsync(HttpMessageInvoker client, SemanticVersion current, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Looppaper", current.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            return FromResponse(current, response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(current, string.Create(CultureInfo.InvariantCulture, $"GitHub did not answer within {timeout.TotalSeconds:0} seconds"));
        }
        catch (OperationCanceledException)
        {
            return Failed(current, "the check was cancelled");
        }
        catch (HttpRequestException ex)
        {
            return Failed(current, ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return Failed(current, ex.Message);
        }
    }

    /// <summary>Turns an HTTP answer from the releases API into a result (no I/O; used by tests).</summary>
    public static UpdateCheckResult FromResponse(SemanticVersion current, HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.NotFound)
        {
            return NoPublicReleases(current);
        }

        if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return Failed(current, $"GitHub refused the request (HTTP {(int)status}), probably its API rate limit; try again later");
        }

        if (status != HttpStatusCode.OK)
        {
            return Failed(current, $"GitHub answered HTTP {(int)status}");
        }

        return GitHubRelease.TryParse(body, out var release, out var error)
            ? Evaluate(current, release)
            : Failed(current, error);
    }

    /// <summary>Compares the running version with a release.</summary>
    public static UpdateCheckResult Evaluate(SemanticVersion current, GitHubRelease release)
    {
        if (release.Draft)
        {
            return NoPublicReleases(current);
        }

        if (!SemanticVersion.TryParse(release.TagName, out var latest))
        {
            return Failed(current, $"the latest release tag '{release.TagName}' is not a version");
        }

        // Pre-releases are only offered to people already running a pre-release.
        if (latest.IsPrerelease && !current.IsPrerelease)
        {
            return new UpdateCheckResult(UpdateStatus.UpToDate, current, latest, release.HtmlUrl,
                $"Looppaper {current} is up to date (the newest release, {latest}, is a pre-release).");
        }

        if (latest > current)
        {
            return new UpdateCheckResult(UpdateStatus.UpdateAvailable, current, latest, release.HtmlUrl,
                $"Looppaper {latest} is available (you have {current}): {release.HtmlUrl}");
        }

        var message = latest == current
            ? $"Looppaper {current} is up to date."
            : $"Looppaper {current} is newer than the latest release ({latest}).";
        return new UpdateCheckResult(UpdateStatus.UpToDate, current, latest, release.HtmlUrl, message);
    }

    private static UpdateCheckResult NoPublicReleases(SemanticVersion current) =>
        new(UpdateStatus.NoPublicReleases, current, null, null,
            $"No public releases found at {ReleasesPageUrl} (nothing released yet, or the repository is private); you have {current}.");

    private static UpdateCheckResult Failed(SemanticVersion current, string reason) =>
        new(UpdateStatus.Failed, current, null, null, $"Update check failed: {reason}.");
}

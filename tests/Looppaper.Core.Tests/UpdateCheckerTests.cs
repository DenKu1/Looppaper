using System.Net;
using System.Text;
using Looppaper.Core.Updates;

namespace Looppaper.Core.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("V0.10.0", 0, 10, 0, "")]
    [InlineData(" 1.2.3 ", 1, 2, 3, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("1.2.3.0", 1, 2, 3, "")]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.2.3+043e34c26eab", 1, 2, 3, "")]
    [InlineData("0.2.0-test.1+043e34c", 0, 2, 0, "test.1")]
    public void TryParse_AcceptsVersions(string text, int major, int minor, int patch, string prerelease)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, version.Prerelease);
        Assert.Equal(prerelease.Length > 0, version.IsPrerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.x.3")]
    [InlineData("1..3")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.2.3-beta_1")]
    [InlineData("99999999999.0.0")]
    [InlineData("latest")]
    public void TryParse_RejectsNonVersions(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.0.9", "1.0.10")]
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("0.9.9", "1.0.0-rc.1")]
    public void Compare_FollowsSemVerPrecedence(string lower, string higher)
    {
        Assert.True(SemanticVersion.TryParse(lower, out var a));
        Assert.True(SemanticVersion.TryParse(higher, out var b));

        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.True(a != b);
    }

    [Fact]
    public void Equality_IgnoresLeadingVAndBuildMetadata()
    {
        Assert.True(SemanticVersion.TryParse("v1.2.3+abc", out var a));
        Assert.True(SemanticVersion.TryParse("1.2.3", out var b));

        Assert.True(a == b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal("1.2.3", a.ToString());
    }

    [Fact]
    public void FromAssembly_ReadsTheBuildVersion()
    {
        var version = SemanticVersion.FromAssembly(typeof(SemanticVersion).Assembly);

        Assert.True(version.Major >= 0);
        Assert.DoesNotContain("+", version.ToString(), StringComparison.Ordinal);
    }
}

public class GitHubReleaseTests
{
    // Trimmed copy of a real "GET /repos/{owner}/{repo}/releases/latest" response.
    private const string ReleaseJson = """
        {
          "url": "https://api.github.com/repos/DenKu1/Looppaper/releases/1",
          "html_url": "https://github.com/DenKu1/Looppaper/releases/tag/v0.2.0",
          "id": 1,
          "author": { "login": "DenKu1" },
          "tag_name": "v0.2.0",
          "target_commitish": "main",
          "name": "Looppaper 0.2.0",
          "draft": false,
          "prerelease": false,
          "created_at": "2026-10-20T10:00:00Z",
          "published_at": "2026-10-20T10:05:00Z",
          "assets": [ { "name": "Looppaper-0.2.0-win-x64.zip", "size": 35428474 } ],
          "body": "Notes"
        }
        """;

    [Fact]
    public void TryParse_ReadsTheReleaseFields()
    {
        Assert.True(GitHubRelease.TryParse(ReleaseJson, out var release, out var error));

        Assert.Equal(string.Empty, error);
        Assert.Equal("v0.2.0", release.TagName);
        Assert.Equal("Looppaper 0.2.0", release.Name);
        Assert.Equal("https://github.com/DenKu1/Looppaper/releases/tag/v0.2.0", release.HtmlUrl);
        Assert.False(release.Prerelease);
        Assert.False(release.Draft);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 5, 0, TimeSpan.Zero), release.PublishedAt);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "tag_name": "v1.0.0" }""")]
    [InlineData("""{ "html_url": "https://example.invalid" }""")]
    [InlineData("""{ "tag_name": 1, "html_url": "https://example.invalid" }""")]
    public void TryParse_RejectsIncompleteReleases(string json)
    {
        Assert.False(GitHubRelease.TryParse(json, out _, out var error));
        Assert.NotEmpty(error);
    }
}

public class UpdateCheckerTests
{
    private static SemanticVersion V(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        return version;
    }

    private static GitHubRelease Release(string tag, bool prerelease = false, bool draft = false) =>
        new(tag, null, "https://github.com/DenKu1/Looppaper/releases/tag/" + tag, prerelease, draft, null);

    private static string Json(string tag, bool prerelease = false) =>
        $$"""{ "tag_name": "{{tag}}", "html_url": "https://github.com/DenKu1/Looppaper/releases/tag/{{tag}}", "prerelease": {{(prerelease ? "true" : "false")}} }""";

    [Fact]
    public void Evaluate_NewerRelease_IsAnUpdateWithItsUrl()
    {
        var result = UpdateChecker.Evaluate(V("0.1.0"), Release("v0.2.0"));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(V("0.2.0"), result.Latest);
        Assert.Equal("https://github.com/DenKu1/Looppaper/releases/tag/v0.2.0", result.ReleaseUrl);
        Assert.Contains("0.2.0 is available", result.Message, StringComparison.Ordinal);
        Assert.Contains("you have 0.1.0", result.Message, StringComparison.Ordinal);
        Assert.Contains(result.ReleaseUrl!, result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.2.0", "v0.2.0")]
    [InlineData("0.3.0", "v0.2.0")]
    [InlineData("0.3.0-beta.1", "v0.2.0")]
    public void Evaluate_SameOrOlderRelease_IsUpToDate(string current, string tag)
    {
        var result = UpdateChecker.Evaluate(V(current), Release(tag));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void Evaluate_ReleaseOfARunningPrerelease_IsAnUpdate()
    {
        var result = UpdateChecker.Evaluate(V("0.2.0-beta.1"), Release("v0.2.0"));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public void Evaluate_PrereleaseIsNotOfferedToReleaseBuilds()
    {
        var result = UpdateChecker.Evaluate(V("0.2.0"), Release("v0.3.0-beta.1", prerelease: true));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Contains("pre-release", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_NewerPrereleaseIsOfferedToPrereleaseBuilds()
    {
        var result = UpdateChecker.Evaluate(V("0.3.0-beta.1"), Release("v0.3.0-beta.2", prerelease: true));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public void Evaluate_DraftCountsAsNoRelease()
    {
        var result = UpdateChecker.Evaluate(V("0.1.0"), Release("v9.0.0", draft: true));

        Assert.Equal(UpdateStatus.NoPublicReleases, result.Status);
    }

    [Fact]
    public void Evaluate_UnparsableTag_Fails()
    {
        var result = UpdateChecker.Evaluate(V("0.1.0"), Release("nightly"));

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("nightly", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromResponse_NotFound_IsNoPublicReleasesNotAnError()
    {
        var result = UpdateChecker.FromResponse(V("0.1.0"), HttpStatusCode.NotFound, """{ "message": "Not Found" }""");

        Assert.Equal(UpdateStatus.NoPublicReleases, result.Status);
        Assert.StartsWith("No public releases found", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "rate limit")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public void FromResponse_OtherErrors_Fail(HttpStatusCode status, string expected)
    {
        var result = UpdateChecker.FromResponse(V("0.1.0"), status, "{}");

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains(expected, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromResponse_InvalidJson_Fails()
    {
        var result = UpdateChecker.FromResponse(V("0.1.0"), HttpStatusCode.OK, "<html>");

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_SendsAGitHubApiRequestAndEvaluatesTheAnswer()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Json("v1.0.0"), Encoding.UTF8, "application/json"),
        }));
        using var client = new HttpClient(handler);

        var result = await UpdateChecker.CheckAsync(client, V("0.1.0"), TimeSpan.FromSeconds(5));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(UpdateChecker.LatestReleaseApiUrl, request.RequestUri!.ToString());
        Assert.Equal("Looppaper/0.1.0", request.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_NotFound_IsNoPublicReleases()
    {
        using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{ "message": "Not Found" }"""),
        })));

        var result = await UpdateChecker.CheckAsync(client, V("0.1.0"), TimeSpan.FromSeconds(5));

        Assert.Equal(UpdateStatus.NoPublicReleases, result.Status);
    }

    [Fact]
    public async Task CheckAsync_NetworkError_FailsWithoutThrowing()
    {
        using var client = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("No such host is known.")));

        var result = await UpdateChecker.CheckAsync(client, V("0.1.0"), TimeSpan.FromSeconds(5));

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("No such host is known", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_Timeout_FailsWithoutThrowing()
    {
        using var client = new HttpClient(new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var result = await UpdateChecker.CheckAsync(client, V("0.1.0"), TimeSpan.FromMilliseconds(50));

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("did not answer", result.Message, StringComparison.Ordinal);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }
}

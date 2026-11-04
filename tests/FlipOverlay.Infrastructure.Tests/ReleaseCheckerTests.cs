using FlipOverlay.Infrastructure;
using Xunit;

namespace FlipOverlay.Infrastructure.Tests;

public sealed class ReleaseCheckerTests
{
    [Fact]
    public async Task Unconfigured_repository_is_disabled_without_network()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("No network"));
        var checker = new ReleaseChecker(new HttpScheduler(new HttpClient(handler), new Clock()), null, "1.0.0", Agent);
        Assert.Equal(ReleaseCheckStatus.Disabled, (await checker.CheckAsync()).Status);
        Assert.Equal(0, handler.Calls);
    }
    [Theory]
    [InlineData("1.9.0", "v1.10.0", ReleaseCheckStatus.UpdateAvailable)]
    [InlineData("1.10.0", "v1.10.0", ReleaseCheckStatus.UpToDate)]
    [InlineData("2.0.0", "v1.10.0", ReleaseCheckStatus.UpToDate)]
    [InlineData("1.10.0-rc.1", "v1.10.0", ReleaseCheckStatus.UpdateAvailable)]
    [InlineData("1.10.0+build.3", "v1.10.0+build.4", ReleaseCheckStatus.UpToDate)]
    [InlineData("1.0.0", "v2.0.0-beta.1", ReleaseCheckStatus.InvalidData)]
    [InlineData("garbage", "v1.10.0", ReleaseCheckStatus.InvalidData)]
    [InlineData("1.0.0", "v01.10.0", ReleaseCheckStatus.InvalidData)]
    [InlineData("1.0.0", "1.2", ReleaseCheckStatus.InvalidData)]
    public async Task Stable_semver_is_compared_numerically(string current, string latest, ReleaseCheckStatus expected)
    {
        var handler = new Handler((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/owner/repo/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Contains("contact@example.test", request.Headers.UserAgent.ToString());
            return Task.FromResult(Handler.Json($"{{\"tag_name\":\"{latest}\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://github.com/owner/repo/releases/tag/{latest}\"}}"));
        });
        var checker = new ReleaseChecker(new HttpScheduler(new HttpClient(handler), new Clock()), "owner/repo", current, Agent);
        var result = await checker.CheckAsync();
        Assert.Equal(expected, result.Status);
        if (expected == ReleaseCheckStatus.UpdateAvailable) Assert.Equal(latest, result.LatestVersion);
    }
    [Theory]
    [InlineData("../repo")]
    [InlineData("owner/repo/extra")]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("owner/repo?query")]
    [InlineData("owner/..")]
    public void Repository_must_be_exact_owner_and_name(string repository)
    {
        Assert.Throws<ArgumentException>(() => new ReleaseChecker(new HttpScheduler(new HttpClient(), new Clock()), repository, "1.0.0", Agent));
    }
    [Theory]
    [InlineData("http://github.com/owner/repo/releases/tag/v2.0.0")]
    [InlineData("https://github.com.evil.test/owner/repo/releases/tag/v2.0.0")]
    [InlineData("https://github.com/other/repo/releases/tag/v2.0.0")]
    [InlineData("https://github.com/owner/repo/releases/tag/v1.0.0")]
    [InlineData("https://github.com/owner/repo/releases/tag/v2.0.0?redirect=bad")]
    [InlineData("https://user@github.com/owner/repo/releases/tag/v2.0.0")]
    [InlineData("https://github.com:444/owner/repo/releases/tag/v2.0.0")]
    public async Task Only_exact_https_github_release_link_is_trusted(string url)
    {
        var handler = new Handler((_, _) => Task.FromResult(Handler.Json($"{{\"tag_name\":\"v2.0.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"{url}\"}}")));
        var checker = new ReleaseChecker(new HttpScheduler(new HttpClient(handler), new Clock()), "owner/repo", "1.0.0", Agent);
        var result = await checker.CheckAsync();
        Assert.Equal(ReleaseCheckStatus.InvalidData, result.Status);
        Assert.Null(result.ReleaseUrl);
    }
    [Fact]
    public async Task Checks_wait_24_hours_then_revalidate_with_etag_without_downloading()
    {
        var clock = new Clock();
        var handler = new Handler((request, _) =>
        {
            if (request.Headers.IfNoneMatch.Count == 0)
                return Task.FromResult(Handler.Json("""{"tag_name":"v2.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/owner/repo/releases/tag/v2.0.0","assets":[{"browser_download_url":"https://evil.test/download.exe"}]}""", etag: "\"release\""));
            Assert.Equal("\"release\"", request.Headers.IfNoneMatch.Single().Tag);
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotModified));
        });
        var checker = new ReleaseChecker(new HttpScheduler(new HttpClient(handler), clock), "owner/repo", "1.0.0", Agent);
        var first = await checker.CheckAsync();
        Assert.Equal(clock.Now.AddHours(24), first.NextCheckAt);
        clock.Advance(86399);
        Assert.Equal(first.CheckedAt, (await checker.CheckAsync()).CheckedAt);
        Assert.Equal(1, handler.Calls);
        clock.Advance(1);
        var rechecked = await checker.CheckAsync();
        Assert.Equal(ReleaseCheckStatus.UpdateAvailable, rechecked.Status);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(clock.Now, rechecked.CheckedAt);
    }
    [Theory]
    [InlineData(System.Net.HttpStatusCode.Forbidden, ReleaseCheckStatus.AccessDenied)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests, ReleaseCheckStatus.RateLimited)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, ReleaseCheckStatus.Offline)]
    public async Task Network_failures_are_not_update_success(System.Net.HttpStatusCode code, ReleaseCheckStatus expected)
    {
        var handler = new Handler((_, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage(code)));
        var checker = new ReleaseChecker(new HttpScheduler(new HttpClient(handler), new Clock()), "owner/repo", "1.0.0", Agent);
        Assert.Equal(expected, (await checker.CheckAsync()).Status);
    }
    private const string Agent = "FlipOverlay/0.1 (contact@example.test)";
}

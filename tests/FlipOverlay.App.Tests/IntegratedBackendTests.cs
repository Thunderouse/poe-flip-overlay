using System.Net;
using FlipOverlay.App;
using FlipOverlay.Infrastructure;
using Xunit;

namespace FlipOverlay.App.Tests;

public sealed class IntegratedBackendTests
{
    [Fact]
    public async Task Aggregate_download_does_not_become_an_observed_or_fake_candidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "flip-integrated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var repository = new LocalRepository(root);
            using var http = new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/leagues")
                ? """[{"id":"Allflame","name":"Allflame"}]"""
                : """{"core":{"primary":"chaos"},"items":[{"id":"divine","name":"Divine Orb"}],"lines":[{"id":"divine","primaryValue":400,"volumePrimaryValue":1200}]}"""));
            var backend = new LocalWorkspaceBackend(repository, http, TimeProvider.System);
            var league = Assert.Single(await backend.GetLeaguesAsync(CancellationToken.None));
            Assert.Equal("Allflame", league.Id);
            var snapshot = await backend.RefreshAsync(new("Allflame", WorkspaceMode.Candidates), CancellationToken.None);
            Assert.Empty(snapshot.Rows);
            Assert.Contains("unverified", snapshot.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Null(snapshot.SourceObservedAt);
            Assert.NotNull(snapshot.FetchedAt);
            Assert.True(snapshot.NextRefreshAt >= snapshot.FetchedAt.Value.AddMinutes(15));
            var observed = await backend.RefreshAsync(new("Allflame", WorkspaceMode.Observed), CancellationToken.None);
            Assert.Empty(observed.Rows);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Two_confirmed_directions_in_same_league_produce_manual_only_route()
    {
        var root = Path.Combine(Path.GetTempPath(), "flip-integrated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var repository = new LocalRepository(root);
            using var http = new HttpClient(new FakeHandler(_ => "[]"));
            var backend = new LocalWorkspaceBackend(repository, http, TimeProvider.System);
            var now = TimeProvider.System.GetUtcNow();
            await backend.SubmitManualAsync(new("Allflame", "chaos", 10, "divine", 1, 1, 10, now), CancellationToken.None);
            Assert.Empty((await backend.RefreshAsync(new("Allflame", WorkspaceMode.Observed), CancellationToken.None)).Rows);
            await backend.SubmitManualAsync(new("Allflame", "divine", 1, "chaos", 12, 12, 20, now), CancellationToken.None);
            var row = Assert.Single((await backend.RefreshAsync(new("Allflame", WorkspaceMode.Observed), CancellationToken.None)).Rows);
            Assert.Equal(2, row.Profit);
            Assert.Equal(30, row.Gold);
            Assert.Contains("Manual", row.Quality);
            Assert.Empty((await backend.RefreshAsync(new("Standard", WorkspaceMode.Observed), CancellationToken.None)).Rows);
            Assert.Empty((await backend.RefreshAsync(new("Allflame", WorkspaceMode.Candidates), CancellationToken.None)).Rows);
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
    }
}

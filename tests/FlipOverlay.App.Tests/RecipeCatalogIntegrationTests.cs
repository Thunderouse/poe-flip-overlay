using System.Net;
using FlipOverlay.Infrastructure;
using Xunit;

namespace FlipOverlay.App.Tests;

public class RecipeCatalogIntegrationTests
{
    [Fact]
    public async Task Catalog_that_expands_when_safely_serialized_is_rejected_before_storage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "flip-catalog-bound-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var repository = new LocalRepository(directory);
            using var http = new HttpClient(new Prices());
            using var backend = new LocalWorkspaceBackend(repository, http, TimeProvider.System);
            var metadata = new string('&', 300);
            var document = new UserRecipeCatalog("v1", "poe1", "pc", "p1", "poe.ninja", "chaos",
                Enumerable.Range(0, 100).Select(i => new UserRecipe("r" + i, "input", 2, "output", 1,
                    metadata, metadata, metadata, metadata, DateTimeOffset.Parse("2026-01-01T00:00:00Z"))).ToArray());
            var input = System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(input) < 256 * 1024);
            await Assert.ThrowsAsync<ArgumentException>(() => backend.ImportCatalogAsync(input, "p1", true, CancellationToken.None));
            Assert.Null(repository.LoadSetting("recipes.catalog"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Explicitly_confirmed_patch_catalog_creates_estimated_candidates_only()
    {
        var directory = Path.Combine(Path.GetTempPath(), "flip-recipes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var repository = new LocalRepository(directory);
            using var http = new HttpClient(new Prices());
            using var backend = new LocalWorkspaceBackend(repository, http, TimeProvider.System);
            IRecipeWorkspaceBackend recipes = backend;
            var catalog = """
            {"version":"fixture-v1","game":"poe1","realm":"pc","patch":"fixture-patch",
             "sourceId":"poe.ninja","settlementId":"chaos","recipes":[
              {"id":"fixture-recipe","inputId":"input","inputUnits":2,"outputId":"output","outputUnits":1,
               "npc":"Fixture NPC","act":"2","condition":"User checked fixture","source":"Manual in-game check",
               "checkedAt":"2026-01-01T00:00:00Z"}]}
            """;
            await Assert.ThrowsAsync<ArgumentException>(() => recipes.ImportCatalogAsync(catalog, "fixture-patch", false, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => recipes.ImportCatalogAsync(catalog, "wrong", true, CancellationToken.None));
            await recipes.ImportCatalogAsync(catalog, "fixture-patch", true, CancellationToken.None);
            var snapshot = await backend.RefreshAsync(new("fixture-league", WorkspaceMode.Candidates, "chaos", 20), CancellationToken.None);
            var row = Assert.Single(snapshot.Rows);
            Assert.Equal(10m, row.Profit);
            Assert.Null(row.Gold);
            Assert.Null(row.ObservedAt);
            Assert.Null(row.Age);
            Assert.Contains("Estimated", row.Quality);
            Assert.Contains("fixture-patch", row.Details);
            Assert.Empty((await backend.RefreshAsync(new("fixture-league", WorkspaceMode.Observed), CancellationToken.None)).Rows);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task Catalog_command_requires_explicit_confirmation_and_exact_patch_before_persisting()
    {
        var backend = new CatalogBackend();
        using var vm = new WorkspaceViewModel(backend);
        vm.CatalogJson = "explicit user JSON";
        vm.CatalogPatch = "user-patch";
        await vm.ImportCatalogCommand.ExecuteAsync(null);
        Assert.False(backend.Imported);
        vm.CatalogConfirmed = true;
        await vm.ImportCatalogCommand.ExecuteAsync(null);
        Assert.True(backend.Imported);
        Assert.False(vm.CatalogConfirmed);
    }
    private sealed class CatalogBackend : IWorkspaceBackend, IRecipeWorkspaceBackend
    {
        public bool Imported { get; private set; }
        public Task ImportCatalogAsync(string json, string patch, bool confirmed, CancellationToken ct)
        { Assert.Equal("explicit user JSON", json); Assert.Equal("user-patch", patch); Assert.True(confirmed); Imported = true; return Task.CompletedTask; }
        public Task<IReadOnlyList<LeagueDto>> GetLeaguesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LeagueDto>>([]);
        public Task<WorkspaceSnapshot> RefreshAsync(WorkspaceRequest r, CancellationToken ct) => Task.FromResult(new WorkspaceSnapshot([], "No quotes"));
        public Task SubmitManualAsync(ManualEntry e, CancellationToken ct) => Task.CompletedTask;
        public Task ExportAsync(string p, WorkspaceRequest r, IReadOnlyList<ResultDto> rows, CancellationToken ct) => Task.CompletedTask;
        public Task<WorkspaceSettings> LoadSettingsAsync(CancellationToken ct) => Task.FromResult(new WorkspaceSettings());
        public Task SaveSettingsAsync(WorkspaceSettings s, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Prices : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
              {"core":{"primary":"chaos"},"items":[{"id":"input","name":"Input"},{"id":"output","name":"Output"}],
              "lines":[{"id":"input","primaryValue":2},{"id":"output","primaryValue":6}]}
              """) });
    }
}

using System.Text.Json;
using FlipOverlay.Core;
using Xunit;

namespace FlipOverlay.Core.Tests;

public class RecipeCatalogTests
{
    // Fictional test-only recipe; never runtime data.
    private const string Json = """
        {"schemaVersion":1,"game":"fixture-game","realm":"pc","patch":"p1","catalogVersion":"v1",
         "recipes":[{"id":"fixture","inputId":"B","outputId":"C","inputUnits":3,"outputUnits":2,
         "patch":"p1","source":"https://example.invalid/evidence","checkedAt":"2026-01-01T11:00:00Z",
         "npc":"fixture NPC","act":"fixture act","condition":"fixture prerequisite"}]}
        """;

    [Fact]
    public void Activation_requires_explicit_current_patch_and_exact_scope()
    {
        Assert.Empty(JsonRecipeCatalog.Load(Json, RouteTests.Now).GetRecipes(RouteTests.Context));
        var catalog = JsonRecipeCatalog.Load(Json, RouteTests.Now, "p1");
        var edge = Assert.Single(catalog.GetRecipes(RouteTests.Context));
        Assert.Equal(new BatchRate(3, 2), edge.Rate);
        Assert.Equal("B", edge.InputId);
        Assert.Equal("C", edge.OutputId); // No invented reverse edge.
        foreach (var context in new[] {
            RouteTests.Context with { Game = "fake-other-profile" }, RouteTests.Context with { Realm = "other" },
            RouteTests.Context with { Patch = "unknown" }, RouteTests.Context with { CatalogVersion = "v2" } })
            Assert.Empty(catalog.GetRecipes(context));
    }

    [Fact]
    public void Fake_profiles_keep_catalogs_and_gold_models_separate()
    {
        IGameProfile first = new FakeProfile("fixture-game", JsonRecipeCatalog.Load(Json, RouteTests.Now, "p1"), new UnknownGoldCostModel());
        IGameProfile second = new FakeProfile("fake-second", JsonRecipeCatalog.Load(
            Json.Replace("fixture-game", "fake-second"), RouteTests.Now, "p1"), new FakeGold());
        Assert.Single(first.Recipes.GetRecipes(RouteTests.Context));
        Assert.Empty(second.Recipes.GetRecipes(RouteTests.Context));
        var secondContext = RouteTests.Context with { Game = second.Game };
        Assert.Single(second.Recipes.GetRecipes(secondContext));
        Assert.Empty(first.Recipes.GetRecipes(secondContext));
        var observation = RouteTests.Quote("q", "A", "B", 1, 1);
        Assert.Null(first.GoldCosts.Calculate(observation, 1, 1).Amount);
        Assert.Equal(7m, second.GoldCosts.Calculate(observation, 1, 1).Amount);
    }

    private sealed record FakeProfile(string Game, IRecipeCatalog Recipes, IGoldCostModel GoldCosts) : IGameProfile;
    private sealed class FakeGold : IGoldCostModel
    {
        public GoldCost Calculate(ManualObservation observation, long input, long output) =>
            new(7, GoldQuality.Estimated, "test only", "p1", RouteTests.Now);
    }

    [Theory]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData("\"inputUnits\":3", "\"inputUnits\":0")]
    [InlineData("\"outputUnits\":2", "\"outputUnits\":1.5")]
    [InlineData("fixture prerequisite", "")]
    [InlineData("fixture NPC", "")]
    [InlineData("fixture act", "")]
    [InlineData("https://example.invalid/evidence", "")]
    [InlineData("2026-01-01T11:00:00Z", "2027-01-01T11:00:00Z")]
    public void Invalid_catalog_fails_closed(string before, string after) =>
        Assert.Throws<JsonException>(() => JsonRecipeCatalog.Load(Json.Replace(before, after), RouteTests.Now, "p1"));
}

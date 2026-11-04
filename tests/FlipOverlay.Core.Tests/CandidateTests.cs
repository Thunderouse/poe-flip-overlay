using FlipOverlay.Core;
using Xunit;

namespace FlipOverlay.Core.Tests;

public class CandidateTests
{
    private static readonly MarketContext Context = RouteTests.Context;
    private static readonly DateTimeOffset Now = RouteTests.Now;
    private static AggregateValuation Price(string id, decimal price, MarketContext? context = null) =>
        new(context ?? Context, id, price, "aggregate fixture", null, Now);
    private static RecipeEdge Recipe(string id, string input, long take, string output, long give, string patch = "p1") =>
        new(id, input, output, new(take, give), patch, "fixture source", Now, "fixture NPC", "fixture condition");

    [Fact]
    public void Settlement_identity_supports_fixed_npc_purchase_without_inventing_a_quote()
    {
        var candidate = Assert.Single(new CandidateEngine().Evaluate(new CandidateRequest(Context, "A", 12),
            [Price("B", 12)], [Recipe("npc-buy", "A", 10, "B", 1)]));
        Assert.Equal(10m, candidate.CapitalRequired);
        Assert.Equal(2m, candidate.EstimatedProfit);
        Assert.Null(candidate.SourcePeriodEnd);
        Assert.Null(candidate.Bid);
        Assert.Null(candidate.Ask);
    }
    [Fact]
    public void Recipe_can_end_in_settlement_identity_without_a_market_sale_quote()
    {
        var candidate = Assert.Single(new CandidateEngine().Evaluate(new CandidateRequest(Context, "A", 10),
            [Price("B", 10)], [Recipe("npc-exit", "B", 1, "A", 12)]));
        Assert.Equal(2m, candidate.EstimatedProfit);
        Assert.Equal("A", candidate.OutputId);
    }

    [Fact]
    public void Candidate_is_estimated_and_never_contains_executable_quotes()
    {
        var result = new CandidateEngine().Evaluate(new CandidateRequest(Context, "A", 20),
            [Price("B", 2), Price("C", 3)], [Recipe("r", "B", 2, "C", 2)]);
        var candidate = Assert.Single(result);
        Assert.Equal("Estimated — verify in game", candidate.Quality);
        Assert.Equal(10m, candidate.EstimatedProfit);
        Assert.Equal(50m, candidate.EstimatedRoiPercent);
        Assert.Null(candidate.Bid);
        Assert.Null(candidate.Ask);
        Assert.Null(candidate.Depth);
        Assert.Null(candidate.SourcePeriodEnd);
        Assert.Equal(Now, candidate.FetchedAt);
    }

    [Theory]
    [InlineData("reverse")]
    [InlineData("wrong-patch")]
    [InlineData("missing-price")]
    [InlineData("wrong-context")]
    [InlineData("zero-price")]
    public void Invalid_or_unpriced_recipe_cannot_create_candidate(string reason)
    {
        var recipes = new[] { reason switch
        {
            "reverse" => Recipe("r", "C", 2, "B", 2),
            "wrong-patch" => Recipe("r", "B", 2, "C", 2, "p2"),
            _ => Recipe("r", "B", 2, "C", 2)
        } };
        var prices = reason switch
        {
            "missing-price" => new[] { Price("B", 2) },
            "wrong-context" => new[] { Price("B", 2), Price("C", 3, Context with { LeagueId = "other" }) },
            "zero-price" => new[] { Price("B", 0), Price("C", 3) },
            _ => new[] { Price("B", 2), Price("C", 3) }
        };
        var candidates = new CandidateEngine().Evaluate(new CandidateRequest(Context, "A", 20), prices, recipes);
        if (reason == "reverse")
            Assert.DoesNotContain(candidates, candidate => candidate.InputId == "B" && candidate.OutputId == "C");
        else Assert.Empty(candidates);
    }

    [Fact]
    public void Remainders_are_not_credited_as_sale_revenue()
    {
        var candidate = Assert.Single(new CandidateEngine().Evaluate(new CandidateRequest(Context, "A", 10),
            [Price("B", 2), Price("C", 2)], [Recipe("r", "B", 3, "C", 2)]));
        Assert.Equal(2, candidate.Remainders["B"]);
        Assert.Equal(10m, candidate.CapitalRequired);
        Assert.Equal(4m, candidate.EstimatedRevenue);
        Assert.Equal(-6m, candidate.EstimatedProfit);
        Assert.True(candidate.EstimatedRoiPercent < 0);
    }

    [Fact]
    public void Four_total_steps_include_buy_and_sell_so_only_two_npc_conversions_fit()
    {
        var edges = new[] { Recipe("one", "B", 1, "C", 2), Recipe("two", "C", 2, "D", 3),
            Recipe("three", "D", 3, "E", 4), Recipe("four", "E", 4, "F", 5), Recipe("five", "F", 5, "G", 6) };
        var prices = new[] { Price("B", 1), Price("C", .4m), Price("D", .3m), Price("E", .2m), Price("F", .3m), Price("G", 1) };
        var result = new CandidateEngine().Evaluate(new CandidateRequest(Context, "A", 10), prices, edges);
        // SPEC §6: buy + NPC conversions + sell must fit in four operations.
        Assert.Contains(result, c => c.InputId == "B" && c.RecipeIds.Count == 2 && c.OutputId == "D");
        Assert.All(result, c => Assert.InRange(c.RecipeIds.Count, 1, 2));
    }

    [Fact]
    public void Different_sources_are_not_combined_into_an_artificial_spread()
    {
        var results = new CandidateEngine().Evaluate(new(Context, "A", 10),
            [Price("B", 1), Price("C", 2) with { SourceId = "another provider" }],
            [Recipe("r", "B", 1, "C", 1)]);
        Assert.Empty(results);
    }

    [Fact]
    public void Old_source_period_is_preserved_despite_fresh_fetch()
    {
        var old = Now.AddHours(-4);
        var result = Assert.Single(new CandidateEngine().Evaluate(new(Context, "A", 10),
            [Price("B", 1) with { SourcePeriodEnd = old }, Price("C", 2) with { SourcePeriodEnd = Now }],
            [Recipe("r", "B", 1, "C", 1)]));
        Assert.Equal(old, result.SourcePeriodEnd);
        Assert.Equal(Now, result.FetchedAt);
    }

    [Theory]
    [InlineData("game")]
    [InlineData("realm")]
    [InlineData("catalog")]
    [InlineData("future")]
    [InlineData("id")]
    [InlineData("unknown-patch")]
    public void Recipe_scope_and_metadata_are_checked_even_without_catalog(string reason)
    {
        var recipe = Recipe("r", "B", 1, "C", 1);
        recipe = reason switch
        {
            "game" => recipe with { Game = "other" },
            "realm" => recipe with { Realm = "other" },
            "catalog" => recipe with { CatalogVersion = "other" },
            "future" => recipe with { CheckedAt = Now.AddDays(1) },
            "id" => recipe with { Id = "" },
            _ => recipe with { Patch = "unknown" }
        };
        var context = reason == "unknown-patch" ? Context with { Patch = "unknown" } : Context;
        Assert.Empty(new CandidateEngine().Evaluate(new(context, "A", 10),
            [Price("B", 1, context), Price("C", 2, context)], [recipe], Now));
    }

    [Fact]
    public void Tiny_price_overflow_is_skipped() =>
        Assert.Empty(new CandidateEngine().Evaluate(new(Context, "A", long.MaxValue),
            [Price("B", .0000000000000000000000000001m), Price("C", 2)],
            [Recipe("r", "B", 1, "C", 1)]));

    [Fact]
    public void Empty_catalog_is_default_and_cannot_invent_recipes()
    {
        IRecipeCatalog catalog = new EmptyRecipeCatalog();
        Assert.Empty(new CandidateEngine().Evaluate(new CandidateRequest(Context, "A", 10),
            [Price("B", 1), Price("C", 2)], catalog.GetRecipes(Context)));
    }

    [Fact]
    public void Profile_provides_isolated_catalog_and_gold_policy()
    {
        IGameProfile profile = new FixtureProfile();
        Assert.Equal(Context.Game, profile.Game);
        Assert.Same(profile.Recipes, profile.Recipes);
        Assert.NotNull(profile.GoldCosts);
    }

    private sealed class FixtureProfile : IGameProfile
    {
        public string Game => Context.Game;
        public IRecipeCatalog Recipes { get; } = new EmptyRecipeCatalog();
        public IGoldCostModel GoldCosts { get; } = new UnknownGoldCostModel();
    }
}

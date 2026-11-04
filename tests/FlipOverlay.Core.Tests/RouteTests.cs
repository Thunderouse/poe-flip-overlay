using FlipOverlay.Core;
using Xunit;

namespace FlipOverlay.Core.Tests;

public class RouteTests
{
    internal static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    internal static readonly MarketContext Context = new("fixture-game", "pc", "fixture-league", "p1", "v1", "npc-exchange");
    internal static ManualObservation Quote(string id, string input, string output, long take, long give, long? depth = 100) =>
        new(id, Context, input, output, [new(id, new(take, give), depth)], Now, Now, "manual fixture", true);
    internal static RouteRequest Request(long budget = 11) => new(Context, "A", budget, TimeSpan.FromMinutes(2));

    [Fact]
    public void Decimal_fees_on_every_leg_are_reserved_in_capital()
    {
        var route = new[] { Quote("buy", "A", "B", 2, 3, 3) with { FeePerBatch = .5m },
            Quote("sell", "B", "A", 3, 3) with { FeePerBatch = .25m } };
        var result = new RouteEngine().Evaluate(Request(), route, Now)!;
        Assert.Equal(8.25m, result.CapitalRequired);
        Assert.Equal(.75m, result.NetProfit);
        Assert.Equal(1.5m, result.Steps[0].Fee);
        Assert.Equal(.75m, result.Steps[1].Fee);
        // Fee reservation must resize, not reject a feasible smaller batch.
        Assert.Equal(5.5m, new RouteEngine().Evaluate(Request(6), route, Now)!.CapitalRequired);
    }

    private sealed class FixtureGold(decimal? rate) : IGoldCostModel
    {
        public GoldCost Calculate(ManualObservation observation, long input, long output) =>
            new(output * rate, rate is null ? GoldQuality.Unknown : GoldQuality.Confirmed, "test only", Context.Patch, Now);
    }

    [Theory]
    [InlineData(2, 12)]
    [InlineData(0, 0)]
    [InlineData(null, null)]
    public void Gold_is_charged_on_both_received_legs_and_unknown_is_not_zero(int? rate, int? expected)
    {
        var route = new[] { Quote("buy", "A", "B", 1, 2, 1), Quote("sell", "B", "A", 1, 2) };
        var engine = new RouteEngine(new FixtureGold(rate));
        var result = engine.Evaluate(Request(), route, Now)!;
        Assert.Equal((decimal?)expected, result.Gold);
        Assert.Equal(expected > 0 ? 3m / expected * 1000m : null, result.ProfitPerThousandGold);
        Assert.Equal(rate is null ? null : 2m * rate, result.Steps[0].Gold);
        Assert.Equal(rate is null ? null : 4m * rate, result.Steps[1].Gold);
        if (rate != 0) Assert.Null(engine.Evaluate(Request() with { GoldBudget = 0 }, route, Now));
    }

    [Theory]
    [InlineData("old")]
    [InlineData("unknown")]
    [InlineData("future")]
    [InlineData("unconfirmed")]
    [InlineData("league")]
    [InlineData("patch")]
    [InlineData("market")]
    [InlineData("direction")]
    [InlineData("one-step")]
    [InlineData("five-steps")]
    [InlineData("unknown-depth")]
    [InlineData("ambiguous-stock")]
    public void Ineligible_observations_never_enter_manual_results(string reason)
    {
        var buy = Quote("buy", "A", "B", 1, 2);
        var sell = Quote("sell", "B", "A", 1, 1);
        buy = reason switch
        {
            "old" => buy with { ObservedAt = Now.AddMinutes(-3), FetchedAt = Now },
            "unknown" => buy with { ObservedAt = null },
            "future" => buy with { ObservedAt = Now.AddSeconds(1) },
            "unconfirmed" => buy with { ConfirmedByUser = false },
            "league" => buy with { Context = Context with { LeagueId = "other" } },
            "patch" => buy with { Context = Context with { Patch = "other" } },
            "market" => buy with { Context = Context with { MarketKind = "aggregate" } },
            "direction" => buy with { InputId = "B", OutputId = "A" },
            "unknown-depth" => buy with { Levels = [new("x", new(1, 2), null)] },
            "ambiguous-stock" => buy with { Levels = [new("sell", new(1, 2), 100)] },
            _ => buy
        };
        ManualObservation[] route = reason switch
        {
            "one-step" => [buy],
            "five-steps" => [buy, sell, buy, sell, buy],
            _ => [buy, sell]
        };
        Assert.Null(new RouteEngine().Evaluate(Request(), route, Now));
    }

    [Fact]
    public void Depth_levels_share_stock_across_repeated_legs()
    {
        var buy = Quote("buy", "A", "B", 2, 3, 2) with
        { Levels = [new("shared", new(2, 3), 2), new("other", new(3, 2), 1)] };
        var sell = Quote("sell", "B", "A", 1, 2);
        Assert.Null(new RouteEngine().Evaluate(Request(20), [buy, sell, buy, sell], Now));
        var result = new RouteEngine().Evaluate(Request(20), [buy, sell], Now)!;
        Assert.Equal(8, result.Steps[0].Output);
        Assert.Equal(13, result.Remainders["A"]);
    }

    [Fact]
    public void Two_leg_route_uses_integer_batches_and_keeps_unvalued_remainders()
    {
        var result = new RouteEngine().Evaluate(Request(), [Quote("buy", "A", "B", 3, 2), Quote("sell", "B", "A", 4, 7)], Now)!;
        Assert.Equal(6m, result.CapitalRequired);
        Assert.Equal(7m, result.Revenue);
        Assert.Equal(1m, result.NetProfit);
        Assert.Equal(1m / 6m * 100m, result.RoiPercent);
        Assert.Equal(5, result.Remainders["A"]);
        Assert.Equal(0, result.Remainders["B"]);
        Assert.Equal(4, result.Steps[0].Output);
        Assert.Equal("Manual — verify in game", result.Quality);
    }
}

using FlipOverlay.Core;
using Xunit;

namespace FlipOverlay.Core.Tests;

public class HardeningTests
{
    private sealed class FixedGold(GoldCost cost) : IGoldCostModel
    {
        public GoldCost Calculate(ManualObservation observation, long input, long output) => cost;
    }

    [Fact]
    public void Downstream_depth_sizes_purchase_to_sellable_quantity()
    {
        var buy = RouteTests.Quote("buy", "A", "B", 10, 1) with { Levels = [new("buy-stock", new(10, 1), 100)] };
        var sell = RouteTests.Quote("sell", "B", "A", 1, 12) with { Levels = [new("sell-stock", new(1, 12), 1)] };
        var result = new RouteEngine().Evaluate(RouteTests.Request(1000), [buy, sell], RouteTests.Now);
        Assert.NotNull(result);
        Assert.Equal(10m, result.CapitalRequired);
        Assert.Equal(12m, result.Revenue);
        Assert.Equal(2m, result.NetProfit);
    }

    [Fact]
    public void Integer_lot_remainders_do_not_hide_a_smaller_profitable_batch()
    {
        var result = new RouteEngine().Evaluate(RouteTests.Request(12),
            [RouteTests.Quote("buy", "A", "B", 3, 2), RouteTests.Quote("sell", "B", "A", 3, 5)], RouteTests.Now);
        Assert.NotNull(result);
        Assert.Equal(9m, result.CapitalRequired);
        Assert.Equal(10m, result.Revenue);
        Assert.Equal(1m, result.NetProfit);
    }

    [Fact]
    public void Rounding_phase_can_prefer_an_earlier_complete_lot()
    {
        var result = new RouteEngine().Evaluate(RouteTests.Request(15),
            [RouteTests.Quote("buy", "A", "B", 3, 2), RouteTests.Quote("sell", "B", "A", 3, 5)], RouteTests.Now);
        Assert.NotNull(result);
        Assert.Equal(9m, result.CapitalRequired);
        Assert.Equal(1m, result.NetProfit);
    }

    [Fact]
    public void Worse_downstream_price_levels_do_not_hide_a_profitable_first_level()
    {
        var buy = RouteTests.Quote("buy", "A", "B", 10, 1);
        var sell = RouteTests.Quote("sell", "B", "A", 1, 12) with
        { Levels = [new("good", new(1, 12), 1), new("bad", new(1, 1), 100)] };
        var result = new RouteEngine().Evaluate(RouteTests.Request(100), [buy, sell], RouteTests.Now);
        Assert.NotNull(result);
        Assert.Equal(10m, result.CapitalRequired);
        Assert.Equal(2m, result.NetProfit);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("future")]
    [InlineData("estimated")]
    [InlineData("source")]
    [InlineData("game")]
    [InlineData("realm")]
    [InlineData("catalog")]
    public void Unverified_gold_is_unknown_not_confirmed(string reason)
    {
        var cost = new GoldCost(2, GoldQuality.Confirmed, "fixture", "p1", RouteTests.Now);
        cost = reason switch
        {
            "negative" => cost with { Amount = -2 },
            "future" => cost with { CheckedAt = RouteTests.Now.AddDays(1) },
            "estimated" => cost with { Quality = GoldQuality.Estimated },
            "game" => cost with { Game = "other" },
            "realm" => cost with { Realm = "other" },
            "catalog" => cost with { CatalogVersion = "other" },
            _ => cost with { Source = "" }
        };
        var engine = new RouteEngine(new FixedGold(cost));
        var route = new[] { RouteTests.Quote("buy", "A", "B", 1, 2), RouteTests.Quote("sell", "B", "A", 1, 1) };
        var result = engine.Evaluate(RouteTests.Request(), route, RouteTests.Now)!;
        Assert.Null(result.Gold);
        Assert.Null(engine.Evaluate(RouteTests.Request() with { GoldBudget = 100 }, route, RouteTests.Now));
    }

    [Fact]
    public void Arithmetic_overflow_is_ineligible_not_an_exception() =>
        Assert.Null(new RouteEngine().Evaluate(RouteTests.Request(long.MaxValue),
            [RouteTests.Quote("buy", "A", "B", 1, long.MaxValue), RouteTests.Quote("sell", "B", "A", 1, 1)], RouteTests.Now));

    [Fact]
    public void Gold_budget_selects_smaller_batch()
    {
        var engine = new RouteEngine(new ReceivedGold());
        var result = engine.Evaluate(RouteTests.Request(10) with { GoldBudget = 6 },
            [RouteTests.Quote("buy", "A", "B", 1, 2), RouteTests.Quote("sell", "B", "A", 1, 1)], RouteTests.Now)!;
        Assert.Equal(1m, result.CapitalRequired);
        Assert.Equal(4m, result.Gold);
    }

    private sealed class ReceivedGold : IGoldCostModel
    {
        public GoldCost Calculate(ManualObservation observation, long input, long output) =>
            new(output, GoldQuality.Confirmed, "fixture", "p1", RouteTests.Now);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("stock")]
    [InlineData("context")]
    [InlineData("fetch")]
    public void Manual_metadata_is_required(string reason)
    {
        var buy = RouteTests.Quote("buy", "A", "B", 1, 2);
        var request = RouteTests.Request();
        if (reason == "source") buy = buy with { Source = "" };
        if (reason == "stock") buy = buy with { Levels = [new("", new(1, 2), 100)] };
        if (reason == "context")
        {
            var context = request.Context with { Game = "" };
            request = request with { Context = context };
            buy = buy with { Context = context };
        }
        if (reason == "fetch") buy = buy with { FetchedAt = RouteTests.Now.AddHours(-1) };
        var sell = RouteTests.Quote("sell", "B", "A", 1, 1) with { Context = request.Context };
        Assert.Null(new RouteEngine().Evaluate(request, [buy, sell], RouteTests.Now));
    }

    [Fact]
    public void Repeated_currency_cycle_is_not_a_four_step_opportunity()
    {
        var buy = RouteTests.Quote("buy", "A", "B", 1, 2);
        var sell = RouteTests.Quote("sell", "B", "A", 1, 1);
        Assert.Null(new RouteEngine().Evaluate(RouteTests.Request(), [buy, sell, buy, sell], RouteTests.Now));
    }

    [Fact]
    public void Negative_gold_budget_is_invalid_even_when_gold_is_unknown() =>
        Assert.Null(new RouteEngine().Evaluate(RouteTests.Request() with { GoldBudget = -1 },
            [RouteTests.Quote("buy", "A", "B", 1, 2), RouteTests.Quote("sell", "B", "A", 1, 1)], RouteTests.Now));

    [Fact]
    public void Fee_budget_selects_smaller_executable_batch()
    {
        var result = new RouteEngine().Evaluate(RouteTests.Request(6),
            [RouteTests.Quote("buy", "A", "B", 2, 3) with { FeePerBatch = .5m },
             RouteTests.Quote("sell", "B", "A", 3, 3) with { FeePerBatch = .25m }], RouteTests.Now);
        Assert.NotNull(result);
        Assert.Equal(5.5m, result.CapitalRequired);
        Assert.Equal(6m, result.Revenue);
    }
}

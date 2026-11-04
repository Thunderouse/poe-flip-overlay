using System.Net;
using FlipOverlay.Infrastructure;
using Xunit;

namespace FlipOverlay.Infrastructure.Tests;

public sealed class ProviderTests
{
    [Fact]
    public async Task Ninja_exchange_is_aggregate_with_unknown_source_time_and_cached_fetch_time()
    {
        var clock = new Clock();
        var handler = new Handler((req, _) =>
        {
            Assert.Equal("/poe1/api/economy/exchange/current/overview?league=Hardcore%20Allflame&type=Currency", req.RequestUri!.PathAndQuery);
            Assert.Contains("contact@example.test", req.Headers.UserAgent.ToString());
            return Task.FromResult(Handler.Json("""{"core":{"primary":"chaos"},"lines":[{"id":"chaos-orb","primaryValue":2.5,"volumePrimaryValue":100,"maxVolumeRate":3}],"items":[{"id":"chaos-orb","name":"Chaos Orb","category":"Currency"}]}""", 60, "\"v1\""));
        });
        var provider = new PoeNinjaEconomyProvider(new HttpScheduler(new HttpClient(handler), clock), "FlipOverlay/0.1 (contact@example.test)");
        var first = await provider.GetCurrencyAsync("Hardcore Allflame");
        Assert.Equal(FetchStatus.Success, first.Status);
        Assert.Equal("poe.ninja", first.SourceId);
        Assert.Single(first.Entries);
        Assert.Equal(2.5m, first.Entries[0].ChaosValue);
        Assert.Null(first.Entries[0].ObservedAt);
        Assert.Null(first.Entries[0].Bid);
        Assert.Null(first.Entries[0].Ask);
        Assert.Null(first.Entries[0].Depth);
        Assert.Equal(DataQuality.HistoricalAggregate, first.Quality);
        Assert.Equal(clock.Now, first.FetchedAt);
        var cached = await provider.GetCurrencyAsync("Hardcore Allflame");
        Assert.Equal(FetchStatus.Cached, cached.Status);
        Assert.Equal(first.FetchedAt, cached.FetchedAt);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Watch_keeps_canonical_window_side_timestamp_and_last_trade_distinct()
    {
        var clock = new Clock();
        var handler = new Handler((req, _) =>
        {
            Assert.Equal("/exchange/ratios?league=Hardcore%20Allflame&game=poe1", req.RequestUri!.PathAndQuery);
            Assert.Contains("contact@example.test", req.Headers.UserAgent.ToString());
            return Task.FromResult(Handler.Json("""{"items":[{"id":8360,"name":"Alchemy Shard","category":"currency","price":{"chaos":3.25,"method":"volumeWeightedMean","lowConfidence":true,"lastTradeAt":1793638800,"window":{"start":1793127929,"end":1793732729,"hours":168,"volume":8}},"chaos":{"value":3,"timestamp":1791752400,"lastTradeAt":1791700000,"volume":2}}]}"""));
        });
        var provider = new PoeWatchProvider(new HttpScheduler(new HttpClient(handler), clock), "FlipOverlay/0.1 (contact@example.test)");
        var snapshot = await provider.GetCurrencyAsync("Hardcore Allflame");
        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal(3.25m, entry.ChaosValue);
        Assert.Equal("volumeWeightedMean", entry.Method);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793127929), entry.PeriodStart);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793732729), entry.PeriodEnd);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793638800), entry.LastTradeAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791752400), entry.SideTimestamp);
        Assert.Equal(3m, entry.SideValue);
        Assert.True(entry.LowConfidence);
        Assert.Null(entry.ObservedAt);
        Assert.Null(entry.Depth);
        Assert.Equal(DataQuality.HistoricalAggregate, snapshot.Quality);
    }

    [Fact]
    public async Task Ggg_history_uses_explicit_cursor_and_never_converts_stock_to_depth()
    {
        var clock = new Clock();
        var handler = new Handler((req, _) =>
        {
            Assert.Equal("/api/currency-exchange/1722027600", req.RequestUri!.AbsolutePath);
            Assert.Contains("contact@example.test", req.Headers.UserAgent.ToString());
            return Task.FromResult(Handler.Json("""{"next_change_id":1722031200,"markets":[{"league":"Settlers","market_id":"a|b","market_pair":["a","b"],"volume_traded":{"a":2},"lowest_stock":{"a":10},"highest_stock":{"a":20},"lowest_ratio":{"a":3},"highest_ratio":{"a":4}}]}"""));
        });
        var provider = new GggExchangeHistoryProvider(new HttpScheduler(new HttpClient(handler), clock), "FlipOverlay/0.1 (contact@example.test)");
        var snapshot = await provider.GetHourAsync(1722027600);
        Assert.Equal(1722031200L, snapshot.NextChangeId);
        var market = Assert.Single(snapshot.Markets);
        Assert.Equal("Settlers", market.LeagueId);
        Assert.Equal(2L, market.VolumeTraded["a"]);
        Assert.Equal(10L, market.LowestStock["a"]);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1722027600), snapshot.PeriodStart);
        Assert.Equal(DataQuality.HistoricalAggregate, snapshot.Quality);
        Assert.Null(market.CurrentDepth);
        Assert.Null(market.Bid);
        Assert.Null(market.Ask);
        Assert.Equal(FetchStatus.Success, snapshot.Status);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"primary\":\"divine\"}")]
    [InlineData("{\"primary\":\"unknown\",\"rates\":{\"chaos\":2}}")]
    public async Task Ninja_never_labels_unknown_primary_as_chaos(string core)
    {
        var body = "{\"core\":" + core + ",\"lines\":[{\"id\":\"x\",\"primaryValue\":2}],\"items\":[{\"id\":\"x\",\"name\":\"X\"}]}";
        var scheduler = new HttpScheduler(new HttpClient(new Handler((_, _) => Task.FromResult(Handler.Json(body)))), new Clock());
        var result = await new PoeNinjaEconomyProvider(scheduler, "FlipOverlay/0.1 (contact@example.test)").GetCurrencyAsync("Standard");
        Assert.Equal(FetchStatus.InvalidData, result.Status);
        Assert.Empty(result.Entries);
    }
    [Fact]
    public async Task Ggg_at_end_waits_until_next_utc_hour_and_preserves_fetch_time()
    {
        var clock = new Clock { Now = new DateTimeOffset(2026, 11, 4, 12, 37, 0, TimeSpan.Zero) };
        var handler = new Handler((_, _) => Task.FromResult(Handler.Json("""{"next_change_id":1722027600,"markets":[]}""")));
        var provider = new GggExchangeHistoryProvider(new HttpScheduler(new HttpClient(handler), clock), "FlipOverlay/0.1 (contact@example.test)");
        var first = await provider.GetHourAsync(1722027600);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 13, 0, 0, TimeSpan.Zero), first.NextAllowedAt);
        clock.Advance(60);
        var second = await provider.GetHourAsync(1722027600);
        Assert.Equal(first.FetchedAt, second.FetchedAt);
        Assert.Equal(1, handler.Calls);
        clock.Advance(22 * 60);
        await provider.GetHourAsync(1722027600);
        Assert.Equal(2, handler.Calls);
    }
    [Theory]
    [InlineData(-3600)]
    [InlineData(1722027601)]
    [InlineData(4294969200)]
    public async Task Ggg_rejects_invalid_input_cursor(long cursor)
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("Must not send"));
        var provider = new GggExchangeHistoryProvider(new HttpScheduler(new HttpClient(handler), new Clock()), "FlipOverlay/0.1 (contact@example.test)");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.GetHourAsync(cursor));
        Assert.Equal(0, handler.Calls);
    }
    [Theory]
    [InlineData(1722027601)]
    [InlineData(1722024000)]
    [InlineData(9223372036854775807)]
    public async Task Ggg_rejects_malformed_or_backwards_response_cursor(long next)
    {
        var handler = new Handler((_, _) => Task.FromResult(Handler.Json($"{{\"next_change_id\":{next},\"markets\":[]}}")));
        var provider = new GggExchangeHistoryProvider(new HttpScheduler(new HttpClient(handler), new Clock()), "FlipOverlay/0.1 (contact@example.test)");
        Assert.Equal(FetchStatus.InvalidData, (await provider.GetHourAsync(1722027600)).Status);
    }
    [Fact]
    public async Task Wealthy_is_unsupported_without_network_access()
    {
        var result = await new WealthyExileProvider().GetCurrencyAsync("Standard");
        Assert.Equal(FetchStatus.Unsupported, result.Status);
        Assert.Empty(result.Entries);
    }
}

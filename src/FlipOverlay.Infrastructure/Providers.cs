using System.Text.Json;

namespace FlipOverlay.Infrastructure;

public enum DataQuality { HistoricalAggregate, Unknown }
public sealed record AggregateEntry(
    string Id, string Name, decimal? ChaosValue, decimal? Volume, string? Method = null,
    DateTimeOffset? PeriodStart = null, DateTimeOffset? PeriodEnd = null,
    DateTimeOffset? LastTradeAt = null, DateTimeOffset? SideTimestamp = null,
    decimal? SideValue = null, bool? LowConfidence = null)
{
    // Aggregated mean prices and historical volumes are never executable offers.
    public DateTimeOffset? ObservedAt => null;
    public decimal? Bid => null;
    public decimal? Ask => null;
    public long? Depth => null;
}
public sealed record AggregateSnapshot(string SourceId, string LeagueId, FetchStatus Status,
    DateTimeOffset? FetchedAt, IReadOnlyList<AggregateEntry> Entries,
    DataQuality Quality = DataQuality.HistoricalAggregate, DateTimeOffset? NextAllowedAt = null,
    bool IsStale = false);

internal static class ProviderJson
{
    public static bool Has(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _);
    public static JsonElement Field(JsonElement element, string name) => Has(element, name) ? element.GetProperty(name) : default;
    public static string? Text(JsonElement element, string name) => Field(element, name) is var v && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public static decimal? Number(JsonElement element, string name) => Field(element, name) is var v && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? n : null;
    public static long? Integer(JsonElement element, string name) => Field(element, name) is var v && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    public static DateTimeOffset? Unix(JsonElement element, string name)
    {
        var value = Integer(element, name);
        if (value is null or <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(value.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
    public static bool? Boolean(JsonElement element, string name) => Field(element, name) is var v && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    public static Uri Uri(string host, string path, string league) => new($"{host}{path}{System.Uri.EscapeDataString(league)}");
    public static void CheckLeague(string league)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(league);
        if (league.Length > 200) throw new ArgumentException("League ID too long", nameof(league));
    }
    public static void CheckAgent(string agent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agent);
        if (agent.Contains('\r') || agent.Contains('\n')) throw new ArgumentException("Invalid User-Agent", nameof(agent));
    }
}

public sealed class PoeNinjaEconomyProvider
{
    private readonly HttpScheduler scheduler;
    private readonly string userAgent;

    public PoeNinjaEconomyProvider(HttpScheduler scheduler, string userAgent)
    {
        this.scheduler = scheduler;
        ProviderJson.CheckAgent(userAgent);
        this.userAgent = userAgent;
    }
    public async Task<AggregateSnapshot> GetCurrencyAsync(string leagueId, bool manual = false, CancellationToken cancellationToken = default)
    {
        ProviderJson.CheckLeague(leagueId);
        var uri = ProviderJson.Uri("https://poe.ninja", "/poe1/api/economy/exchange/current/overview?league=", leagueId);
        uri = new Uri(uri + "&type=Currency");
        var response = await scheduler.GetAsync("poe.ninja", uri, manual, cancellationToken, userAgent);
        var entries = new List<AggregateEntry>();
        try
        {
            if (response.Body is not null)
            {
                using var json = JsonDocument.Parse(response.Body);
                var root = json.RootElement;
                if (!ProviderJson.Has(root, "core") || ProviderJson.Field(root, "lines").ValueKind != JsonValueKind.Array || ProviderJson.Field(root, "items").ValueKind != JsonValueKind.Array) throw new JsonException("Invalid exchange overview");
                if (ProviderJson.Text(ProviderJson.Field(root, "core"), "primary") != "chaos")
                    throw new JsonException("Unsupported primary currency; refusing unverified chaos conversion");
                var names = ProviderJson.Field(root, "items").EnumerateArray().Where(x => ProviderJson.Text(x, "id") is not null).ToDictionary(x => ProviderJson.Text(x, "id")!, x => ProviderJson.Text(x, "name") ?? ProviderJson.Text(x, "id")!);
                foreach (var line in ProviderJson.Field(root, "lines").EnumerateArray())
                {
                    var id = ProviderJson.Text(line, "id");
                    if (id is not null && names.TryGetValue(id, out var name)) entries.Add(new(id, name, ProviderJson.Number(line, "primaryValue"), ProviderJson.Number(line, "volumePrimaryValue")));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { return new("poe.ninja", leagueId, FetchStatus.InvalidData, response.FetchedAt, [], NextAllowedAt: response.NextAllowedAt); }
        return new("poe.ninja", leagueId, response.Status, response.FetchedAt, entries, NextAllowedAt: response.NextAllowedAt, IsStale: response.IsStale);
    }
}

public sealed class PoeWatchProvider
{
    private readonly HttpScheduler scheduler;
    private readonly string userAgent;
    public PoeWatchProvider(HttpScheduler scheduler, string userAgent)
    { this.scheduler = scheduler; ProviderJson.CheckAgent(userAgent); this.userAgent = userAgent; }
    public async Task<AggregateSnapshot> GetCurrencyAsync(string leagueId, bool manual = false, CancellationToken cancellationToken = default)
    {
        ProviderJson.CheckLeague(leagueId);
        var uri = new Uri(ProviderJson.Uri("https://api.poe.watch", "/exchange/ratios?league=", leagueId) + "&game=poe1");
        var response = await scheduler.GetAsync("poe.watch", uri, manual, cancellationToken, userAgent);
        var entries = new List<AggregateEntry>();
        try
        {
            if (response.Body is not null)
            {
                using var json = JsonDocument.Parse(response.Body);
                var array = ProviderJson.Field(json.RootElement, "items");
                if (array.ValueKind != JsonValueKind.Array) throw new JsonException("Missing items");
                foreach (var item in array.EnumerateArray())
                {
                    if (ProviderJson.Text(item, "category") != "currency") continue;
                    var id = ProviderJson.Integer(item, "id");
                    var name = ProviderJson.Text(item, "name");
                    if (id is null || name is null) continue;
                    var price = ProviderJson.Field(item, "price");
                    var window = ProviderJson.Field(price, "window");
                    var side = ProviderJson.Field(item, "chaos");
                    entries.Add(new(id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), name,
                        ProviderJson.Number(price, "chaos"), ProviderJson.Number(window, "volume"),
                        ProviderJson.Text(price, "method"), ProviderJson.Unix(window, "start"),
                        ProviderJson.Unix(window, "end"), ProviderJson.Unix(price, "lastTradeAt"),
                        ProviderJson.Unix(side, "timestamp"), ProviderJson.Number(side, "value"),
                        ProviderJson.Boolean(price, "lowConfidence")));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { return new("poe.watch", leagueId, FetchStatus.InvalidData, response.FetchedAt, [], NextAllowedAt: response.NextAllowedAt); }
        return new("poe.watch", leagueId, response.Status, response.FetchedAt, entries, NextAllowedAt: response.NextAllowedAt, IsStale: response.IsStale);
    }
}

public sealed record HistoricalMarket(string LeagueId, string MarketId, IReadOnlyList<string> Pair,
    IReadOnlyDictionary<string, long> VolumeTraded, IReadOnlyDictionary<string, long> LowestStock,
    IReadOnlyDictionary<string, long> HighestStock)
{
    public long? CurrentDepth => null;
    public decimal? Bid => null;
    public decimal? Ask => null;
}
public sealed record HistorySnapshot(FetchStatus Status, long? NextChangeId, DateTimeOffset? PeriodStart,
    DateTimeOffset? FetchedAt, IReadOnlyList<HistoricalMarket> Markets, DataQuality Quality = DataQuality.HistoricalAggregate,
    DateTimeOffset? NextAllowedAt = null, bool IsStale = false)
{
    public bool AtEndOfStream(long requestedCursor) => NextChangeId == requestedCursor;
}
public sealed class GggExchangeHistoryProvider
{
    private readonly HttpScheduler scheduler;
    private readonly string userAgent;
    private readonly object endSync = new();
    private (long Generation, long? Cursor, HistorySnapshot Snapshot)? end;
    public GggExchangeHistoryProvider(HttpScheduler scheduler, string userAgent)
    { this.scheduler = scheduler; ProviderJson.CheckAgent(userAgent); this.userAgent = userAgent; }
    public async Task<HistorySnapshot> GetHourAsync(long? cursor = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (cursor is not null && !ValidCursor(cursor.Value)) throw new ArgumentOutOfRangeException(nameof(cursor));
        var generation = scheduler.Generation;
        lock (endSync)
            if (end is { } cached && cached.Generation == generation && cached.Cursor == cursor && cached.Snapshot.NextAllowedAt > scheduler.UtcNow)
                return cached.Snapshot with { Status = FetchStatus.Cached };
        // Null is explicitly the *first* historical hour, never a latest shortcut.
        var uri = new Uri("https://web.poecdn.com/api/currency-exchange" + (cursor is null ? "" : "/" + cursor.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var response = await scheduler.GetAsync("ggg-history", uri, cancellationToken: cancellationToken, userAgent: userAgent);
        long? next = null;
        var markets = new List<HistoricalMarket>();
        try
        {
            if (response.Body is not null)
            {
                using var json = JsonDocument.Parse(response.Body);
                next = ProviderJson.Integer(json.RootElement, "next_change_id");
                var array = ProviderJson.Field(json.RootElement, "markets");
                if (next is null || !ValidCursor(next.Value) || next < cursor || array.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Invalid history page/cursor");
                foreach (var market in array.EnumerateArray())
                {
                    var pair = ProviderJson.Field(market, "market_pair");
                    if (pair.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid market pair");
                    markets.Add(new(ProviderJson.Text(market, "league") ?? throw new JsonException("league"),
                        ProviderJson.Text(market, "market_id") ?? throw new JsonException("market_id"),
                        pair.EnumerateArray().Select(x => x.GetString() ?? "").ToArray(),
                        Dictionary(market, "volume_traded"), Dictionary(market, "lowest_stock"), Dictionary(market, "highest_stock")));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { return new(FetchStatus.InvalidData, null, null, response.FetchedAt, []); }
        var nextAllowed = response.NextAllowedAt;
        var atEnd = cursor is not null && next == cursor && response.Status is FetchStatus.Success or FetchStatus.Cached or FetchStatus.NotModified;
        if (atEnd)
        {
            var now = scheduler.UtcNow;
            var boundary = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 3600 * 3600).AddHours(1);
            if (nextAllowed is null || nextAllowed < boundary) nextAllowed = boundary;
        }
        var result = new HistorySnapshot(response.Status, next, cursor is null ? null : DateTimeOffset.FromUnixTimeSeconds(cursor.Value), response.FetchedAt, markets, NextAllowedAt: nextAllowed, IsStale: response.IsStale);
        if (atEnd) lock (endSync) end = (generation, cursor, result);
        return result;
    }
    private static bool ValidCursor(long cursor) => cursor > 0 && cursor <= uint.MaxValue && cursor % 3600 == 0;
    private static Dictionary<string, long> Dictionary(JsonElement market, string name)
    {
        var field = ProviderJson.Field(market, name);
        if (field.ValueKind != JsonValueKind.Object) throw new JsonException(name);
        return field.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64());
    }
}
public sealed class WealthyExileProvider
{
    public Task<AggregateSnapshot> GetCurrencyAsync(string leagueId, CancellationToken cancellationToken = default)
        => Task.FromResult(new AggregateSnapshot("wealthy-exile", leagueId, FetchStatus.Unsupported, null, [], DataQuality.Unknown));
}

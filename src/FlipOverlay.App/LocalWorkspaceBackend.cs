using System.Globalization;
using System.Text;
using System.Text.Json;
using FlipOverlay.Core;
using FlipOverlay.Infrastructure;

namespace FlipOverlay.App;

/// <summary>PoE1 aggregate estimates and user-confirmed directions; no live NPC order ingestion.</summary>
public sealed class LocalWorkspaceBackend : IWorkspaceBackend, IRecipeWorkspaceBackend, IDisposable
{
    private const string Agent = "PoE-Flip-Overlay/0.1 (contact: https://github.com/Thunderouse)";
    private readonly LocalRepository repository;
    private readonly HttpScheduler scheduler;
    private readonly PoeNinjaEconomyProvider ninja;
    private readonly TimeProvider time;
    public void Dispose() { repository.Dispose(); }
    public LocalWorkspaceBackend(LocalRepository repository, HttpClient httpClient, TimeProvider time)
    {
        this.repository = repository;
        this.time = time;
        scheduler = new HttpScheduler(httpClient, time);
        ninja = new PoeNinjaEconomyProvider(scheduler, Agent);
    }
    public async Task<IReadOnlyList<LeagueDto>> GetLeaguesAsync(CancellationToken cancellationToken)
    {
        var result = await scheduler.GetAsync("poe.ninja-leagues", new Uri("https://poe.ninja/poe1/api/economy/leagues"),
            cancellationToken: cancellationToken, userAgent: Agent);
        if (result.Status is FetchStatus.AccessDenied or FetchStatus.RateLimited or FetchStatus.Offline or FetchStatus.InvalidData || result.Body is null)
            throw new IOException("League discovery unavailable (" + result.Status + "). Entering a different league automatically is disabled.");
        try
        {
            using var document = JsonDocument.Parse(result.Body);
            var leagues = new List<LeagueDto>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                var name = item.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                // The economy list is a source-owned set; SSF is not an exchange-trading mode.
                if (id.Contains("SSF", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Solo Self-Found", StringComparison.OrdinalIgnoreCase)) continue;
                leagues.Add(new(id, name));
            }
            return leagues;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new InvalidDataException("League response is not a supported economy-leagues document.", e); }
    }
    public async Task<WorkspaceSnapshot> RefreshAsync(WorkspaceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LeagueId)) return new([], "No league selected; no market data requested.");
        if (request.CapitalBudget <= 0 || !double.IsFinite(request.MaximumAgeSeconds) || request.MaximumAgeSeconds <= 0 || request.MaximumAgeSeconds > 86400 || request.MaximumGold is < 0)
            throw new ArgumentException("Enter positive capital and a maximum age of 1–86400 seconds, and optional non-negative gold.");
        if (request.Mode == WorkspaceMode.Observed) return await Task.Run(() => ReadManual(request), cancellationToken);
        if (request.Mode != WorkspaceMode.Candidates) throw new ArgumentOutOfRangeException(nameof(request));
        var snapshot = await ninja.GetCurrencyAsync(request.LeagueId, manual: request.ManualRefresh, cancellationToken);
        var catalogJson = await Task.Run(() => repository.LoadSetting("recipes.catalog"), cancellationToken);
        UserRecipeCatalog? catalog = catalogJson is null ? null : UserRecipeCatalog.Parse(catalogJson, time.GetUtcNow());
        var candidates = new List<ResultDto>();
        if (catalog is not null && (request.SettlementCurrency.Length == 0 || request.SettlementCurrency == catalog.SettlementId) &&
            snapshot.Status is FetchStatus.Success or FetchStatus.Cached or FetchStatus.NotModified && !snapshot.IsStale)
        {
            var context = new MarketContext(catalog.Game, catalog.Realm, request.LeagueId, catalog.Patch, catalog.Version, "npc-exchange-aggregate");
            var valuations = snapshot.Entries.Where(x => x.ChaosValue is > 0).Select(x => new AggregateValuation(context, x.Id,
                x.ChaosValue!.Value, snapshot.SourceId, x.PeriodEnd, snapshot.FetchedAt ?? default));
            var names = snapshot.Entries.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
            var results = new CandidateEngine().Evaluate(new(context, catalog.SettlementId, request.CapitalBudget), valuations, catalog.Edges(), time.GetUtcNow());
            foreach (var result in results.Where(x => x.EstimatedProfit > 0 && request.MaximumGold is null))
            {
                string Name(string id) => names.GetValueOrDefault(id, id);
                var details = string.Join("\n", result.RecipeIds.Select(id => catalog.Recipes.Single(r => r.Id == id))
                    .Select(r => $"{r.InputUnits} {Name(r.InputId)} → {r.OutputUnits} {Name(r.OutputId)}; NPC {r.Npc}, act {r.Act}; conditions: {r.Condition}; source {r.Source}; checked {r.CheckedAt:O}"));
                candidates.Add(new(result.Id, $"Buy {Name(result.InputId)} → NPC conversion → sell {Name(result.OutputId)}",
                    result.EstimatedProfit, result.EstimatedRoiPercent, null, null, result.CapitalRequired,
                    null, "Unknown (aggregate volume is not depth)", snapshot.SourceId, "Estimated — verify in game",
                    $"Catalog {catalog.Version}; patch {catalog.Patch}, user-confirmed; not independently game-verified.\n{details}\n" +
                    $"Estimated outlay {result.CapitalRequired} chaos; estimated revenue {result.EstimatedRevenue} chaos. Gold Unknown.\n" +
                    "Remainders (not credited): " + string.Join(", ", result.Remainders.Select(x => $"{x.Value} {Name(x.Key)}")) +
                    "\nBuy/sell values are aggregates, not orders; stock, execution and fees must be checked in game.", catalog.SettlementId));
            }
        }
        var status = snapshot.Status switch
        {
            FetchStatus.Success or FetchStatus.Cached or FetchStatus.NotModified =>
                $"Fetched {snapshot.Entries.Count} aggregate reference values. Recipe catalog unverified for the current patch; no executable candidates. Verify in game.",
            FetchStatus.RateLimited or FetchStatus.Throttled => "Source limit: wait before refreshing. No newer source data.",
            FetchStatus.AccessDenied => "Source access denied. Requests to this source have stopped.",
            _ => "Source unavailable (" + snapshot.Status + "). No current quotes."
        };
        if (catalog is not null && snapshot.Status is FetchStatus.Success or FetchStatus.Cached or FetchStatus.NotModified)
            status = $"{candidates.Count} estimated candidates from user-confirmed patch {catalog.Patch}. Aggregate prices only; Age unknown, gold and stock Unknown. Verify in game.";
        DateTimeOffset? nextRefresh = snapshot.NextAllowedAt;
        if (snapshot.Status is FetchStatus.Success or FetchStatus.Cached or FetchStatus.NotModified)
        {
            var cadenceFloor = (snapshot.FetchedAt ?? time.GetUtcNow()).AddMinutes(15);
            if (cadenceFloor <= time.GetUtcNow()) cadenceFloor = time.GetUtcNow().AddMinutes(15);
            if (nextRefresh is null || nextRefresh < cadenceFloor) nextRefresh = cadenceFloor;
        }
        return new(candidates, status, snapshot.SourceId, null, snapshot.FetchedAt, nextRefresh,
            "Provider documentation: approximately 15 minutes; source observation age unknown", "NPC Currency Exchange — aggregate analytics");
    }
    public async Task ImportCatalogAsync(string json, string expectedPatch, bool confirmedInGame, CancellationToken cancellationToken)
    {
        if (!confirmedInGame) throw new ArgumentException("Confirm that you checked the directed recipes and access conditions in the selected game patch.");
        var catalog = UserRecipeCatalog.Parse(json, time.GetUtcNow());
        if (!string.Equals(catalog.Patch, expectedPatch, StringComparison.Ordinal))
            throw new ArgumentException("The expected patch does not exactly match the catalog patch.");
        var canonical = JsonSerializer.Serialize(catalog);
        // Validate exactly what reload will parse, not merely the shorter unescaped input.
        UserRecipeCatalog.Parse(canonical, time.GetUtcNow());
        await Task.Run(() => repository.SaveSetting("recipes.catalog", canonical), cancellationToken);
    }
    public async Task SubmitManualAsync(ManualEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(entry.LeagueId) || string.IsNullOrWhiteSpace(entry.GiveCurrency) ||
            string.IsNullOrWhiteSpace(entry.ReceiveCurrency) || entry.GiveCurrency == entry.ReceiveCurrency ||
            !WholePositive(entry.GiveQuantity) || !WholePositive(entry.ReceiveQuantity) ||
            entry.AvailableQuantity is { } available && (!WholePositive(available) || available < entry.ReceiveQuantity) ||
            entry.Gold is { } fee && (fee < 0 || fee > long.MaxValue || decimal.Truncate(fee) != fee) || entry.ObservedAt > time.GetUtcNow())
            throw new ArgumentException("Manual exchange requires different currency IDs, positive whole quantities and valid optional depth, gold and time.");
        await Task.Run(() => repository.SaveManual(new(Guid.NewGuid().ToString("N"), entry.LeagueId, entry.GiveCurrency, entry.GiveQuantity,
            entry.ReceiveCurrency, entry.ReceiveQuantity, entry.AvailableQuantity, entry.Gold, entry.ObservedAt, time.GetUtcNow())), cancellationToken);
    }
    private static bool WholePositive(decimal value) => value > 0 && value <= long.MaxValue && decimal.Truncate(value) == value;
    private WorkspaceSnapshot ReadManual(WorkspaceRequest request)
    {
        var league = request.LeagueId;
        var entries = repository.LoadManual(league);
        var now = time.GetUtcNow();
        var eligible = entries.Where(x => x.ObservedAt is { } at && at <= now && now - at < TimeSpan.FromSeconds(request.MaximumAgeSeconds) &&
            x.AvailableQuantity >= x.ReceiveQuantity).Take(150).ToArray();
        var context = new MarketContext("poe1", "pc", league, "manual", "manual", "npc-exchange");
        var gold = eligible.ToDictionary(x => x.Id, x => x);
        var engine = new RouteEngine(new ManualGoldModel(gold));
        var rows = new List<ResultDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var first in eligible)
        {
            if (rows.Count >= 500) break;
            foreach (var second in eligible)
            {
                if (first.Id == second.Id || first.GiveCurrency != second.ReceiveCurrency ||
                    first.ReceiveCurrency != second.GiveCurrency) continue;
                if (request.SettlementCurrency.Length > 0 && first.GiveCurrency != request.SettlementCurrency) continue;
                var key = first.Id + ":" + second.Id;
                if (seen.Contains(key)) continue;
                var result = engine.Evaluate(new RouteRequest(context, first.GiveCurrency, request.CapitalBudget, TimeSpan.FromSeconds(request.MaximumAgeSeconds), request.MaximumGold),
                    [ToQuote(first, context), ToQuote(second, context)], now);
                if (result is null || result.NetProfit <= 0) continue;
                seen.Add(key);
                var details = string.Join("\n", result.Steps.Select(x =>
                    $"{x.Input} {x.InputId} → {x.Output} {x.OutputId}; unused {x.Remainder} {x.InputId}; gold {x.Gold?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"}; observed {x.ObservedAt:O}"));
                rows.Add(new(result.Id, $"{first.GiveCurrency} → {first.ReceiveCurrency} → {first.GiveCurrency}",
                    result.NetProfit, result.RoiPercent, result.Gold, result.ProfitPerThousandGold,
                    result.CapitalRequired, (decimal)(now - result.OldestObservation).TotalSeconds,
                    "Manual quantity only", "User confirmed", "Manual — verify both legs in game",
                    details + "\nOnly whole confirmed lots within entered stock. Gold per whole lot; no fractional fee scaling. No guaranteed execution. Gold unknown if any leg is unconfirmed.",
                    first.GiveCurrency, result.OldestObservation));
                if (rows.Count >= 500) break;
            }
        }
        return new(rows, rows.Count == 0 ? "No fresh two-leg manual opportunities. Enter and confirm both directions with amounts, stock and times." :
            $"{rows.Count} manually observed route(s). Re-check both legs in game; no automatic execution.",
            "Manual observations", rows.Count == 0 ? null : rows.Min(x => x.ObservedAt),
            entries.Count == 0 ? null : entries.Max(x => x.FetchedAt), null,
            $"Manual; expires {request.MaximumAgeSeconds} seconds after oldest observed leg; evaluation capped at 150 observations / 500 rows", "NPC Currency Exchange — manual");
    }
    private static ManualObservation ToQuote(StoredManualObservation entry, MarketContext context) =>
        new(entry.Id, context, entry.GiveCurrency, entry.ReceiveCurrency,
            [new DepthLevel(entry.Id, new((long)entry.GiveQuantity, (long)entry.ReceiveQuantity), (long)(entry.AvailableQuantity!.Value / entry.ReceiveQuantity))],
            entry.ObservedAt, entry.FetchedAt, "Manual", true);
    private sealed class ManualGoldModel(IReadOnlyDictionary<string, StoredManualObservation> entries) : IGoldCostModel
    {
        public GoldCost Calculate(ManualObservation observation, long input, long output)
        {
            var entry = entries[observation.Id];
            return entry.Gold is { } gold && entry.ObservedAt is { } observed && input % entry.GiveQuantity == 0 &&
                output == input / entry.GiveQuantity * entry.ReceiveQuantity
                ? new(gold * (input / entry.GiveQuantity), GoldQuality.Confirmed, "User-confirmed gold per batch", observation.Context.Patch, observed)
                : new(null, GoldQuality.Unknown, "Gold not entered", observation.Context.Patch, null);
        }
    }
    public Task<WorkspaceSettings> LoadSettingsAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = repository.LoadSetting("workspace");
        try
        {
            return settings is null ? new WorkspaceSettings() :
            JsonSerializer.Deserialize<WorkspaceSettings>(settings) ?? new WorkspaceSettings();
        }
        catch (JsonException) { return new WorkspaceSettings(); }
    }, cancellationToken);
    public Task SaveSettingsAsync(WorkspaceSettings settings, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        repository.SaveSetting("workspace", JsonSerializer.Serialize(settings));
    }, cancellationToken);
    public async Task ExportAsync(string path, WorkspaceRequest request, IReadOnlyList<ResultDto> visibleRows, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a CSV path explicitly.", nameof(path));
        var lines = new StringBuilder("Route,Net profit,Unit,ROI,Gold,Capital,Quality,ObservedAt\r\n");
        foreach (var row in visibleRows)
        {
            if (row.Quality.StartsWith("Manual", StringComparison.Ordinal) != (request.Mode == WorkspaceMode.Observed))
                throw new InvalidOperationException("Export mode and row provenance disagree.");
            string Csv(string? value) => "\"" + (value is { Length: > 0 } && "=+-@".Contains(value[0]) ? "'" : "") + (value ?? "").Replace("\"", "\"\"") + "\"";
            lines.AppendJoin(',', Csv(row.Route), Csv(row.Profit?.ToString(CultureInfo.InvariantCulture)), Csv(row.Unit),
                Csv(row.Roi?.ToString(CultureInfo.InvariantCulture)), Csv(row.Gold?.ToString(CultureInfo.InvariantCulture)),
                Csv(row.Capital?.ToString(CultureInfo.InvariantCulture)), Csv(row.Quality), Csv(row.ObservedAt?.ToString("O")));
            lines.Append("\r\n");
        }
        await File.WriteAllTextAsync(path, lines.ToString(), Encoding.UTF8, cancellationToken);
    }
}

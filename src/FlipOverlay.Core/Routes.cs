namespace FlipOverlay.Core;

public sealed record MarketContext(string Game, string Realm, string LeagueId, string Patch, string CatalogVersion, string MarketKind);
public sealed record DepthLevel(string StockId, BatchRate Rate, long? AvailableBatches);
public sealed record ManualObservation(string Id, MarketContext Context, string InputId, string OutputId,
    IReadOnlyList<DepthLevel> Levels, DateTimeOffset? ObservedAt, DateTimeOffset FetchedAt,
    string Source, bool ConfirmedByUser, decimal FeePerBatch = 0);
public sealed record RouteRequest(MarketContext Context, string SettlementId, long CapitalBudget,
    TimeSpan MaximumAge, decimal? GoldBudget = null);
public sealed record RouteStep(string Id, string InputId, string OutputId, long Input, long Output, long Remainder,
    decimal Fee, decimal? Gold, DateTimeOffset? ObservedAt);
public sealed record RouteResult(string Id, IReadOnlyList<RouteStep> Steps, decimal CapitalRequired,
    decimal Revenue, decimal NetProfit, decimal RoiPercent, IReadOnlyDictionary<string, long> Remainders,
    decimal? Gold, decimal? ProfitPerThousandGold, DateTimeOffset OldestObservation, string Quality);
public sealed class RouteEngine(IGoldCostModel? goldModel = null)
{
    public RouteResult? Evaluate(RouteRequest request, IReadOnlyList<ManualObservation> route, DateTimeOffset now)
    {
        try
        {
            var result = EvaluateBatch(request, route, now, request.CapitalBudget, out var tooLarge);
            if (tooLarge)
            {
                long low = 1, high = request.CapitalBudget - 1;
                result = null;
                while (low <= high)
                {
                    var middle = low + (high - low) / 2;
                    var candidate = EvaluateBatch(request, route, now, middle, out tooLarge);
                    if (tooLarge) high = middle - 1;
                    else { if (candidate is not null) result = candidate; low = middle + 1; }
                }
            }
            if (result is null) return null;
            var best = result;
            // Same sellable output can require less initial spending because downstream depth/lots leave inventory.
            if (result.Steps.Skip(1).Any(step => step.Remainder > 0))
            {
                Consider(MinimumBudgetForOutput(route.Count - 1, result.Steps[^1].Output));
                if (route.Count == 2 && route.All(quote => quote.Levels.Count == 1))
                {
                    var buy = route[0].Levels[0].Rate;
                    var sell = route[1].Levels[0].Rate;
                    var maximumLots = result.Steps[^1].Output / sell.OutputUnits;
                    // Bounded rounding-phase search, not a claim of exhaustive global optimization.
                    for (long offset = 1; offset <= 32 && offset < maximumLots; offset++)
                    {
                        var wanted = new System.Numerics.BigInteger(maximumLots - offset) * sell.InputUnits;
                        var buyLots = (wanted + buy.OutputUnits - 1) / buy.OutputUnits;
                        var spend = buyLots * buy.InputUnits;
                        if (spend > 0 && spend <= request.CapitalBudget)
                            Consider(EvaluateBatch(request, route, now, (long)spend, out _));
                    }
                    var gcd = System.Numerics.BigInteger.GreatestCommonDivisor(buy.OutputUnits, sell.InputUnits);
                    var alignedLots = new System.Numerics.BigInteger(sell.InputUnits) / gcd;
                    var maximumBuyLots = result.Steps[0].Input / buy.InputUnits;
                    var alignedSpend = maximumBuyLots / alignedLots * alignedLots * buy.InputUnits;
                    if (alignedSpend > 0 && alignedSpend <= request.CapitalBudget)
                        Consider(EvaluateBatch(request, route, now, (long)alignedSpend, out _));
                }
            }
            // Depth boundaries are economic breakpoints: later, worse levels must not hide earlier profitable lots.
            for (var stage = 0; stage < route.Count; stage++)
            {
                if (route[stage].Levels.Count <= 1) continue;
                long outputBoundary = 0;
                foreach (var level in route[stage].Levels)
                {
                    if (level.AvailableBatches is not { } count) break;
                    if (count > (long.MaxValue - outputBoundary) / level.Rate.OutputUnits) break;
                    outputBoundary += count * level.Rate.OutputUnits;
                    if (outputBoundary <= 0 || outputBoundary >= result.Steps[stage].Output) break;
                    var candidate = MinimumBudgetForOutput(stage, outputBoundary);
                    if (candidate is not null)
                        Consider(MinimumBudgetForOutput(route.Count - 1, candidate.Steps[^1].Output));
                }
            }
            return best;

            void Consider(RouteResult? candidate)
            {
                if (candidate is not null && (candidate.NetProfit > best.NetProfit ||
                    candidate.NetProfit == best.NetProfit && candidate.CapitalRequired < best.CapitalRequired)) best = candidate;
            }
            RouteResult? MinimumBudgetForOutput(int stage, long target)
            {
                long low = 1, high = (long)decimal.Floor(result.CapitalRequired);
                RouteResult? minimum = null;
                while (low <= high)
                {
                    var middle = low + (high - low) / 2;
                    var candidate = EvaluateBatch(request, route, now, middle, out var oversized);
                    if (oversized) { high = middle - 1; continue; }
                    if (candidate is not null && candidate.Steps[stage].Output >= target)
                    { minimum = candidate; high = middle - 1; }
                    else low = middle + 1;
                }
                return minimum;
            }
        }
        catch (OverflowException) { return null; }
    }

    private RouteResult? EvaluateBatch(RouteRequest request, IReadOnlyList<ManualObservation> route,
        DateTimeOffset now, long sizingBudget, out bool tooLarge)
    {
        tooLarge = false;
        if (!Metadata.Valid(request.Context) || !Metadata.Text(request.SettlementId) || request.GoldBudget < 0) return null;
        if (request.CapitalBudget <= 0 || request.MaximumAge <= TimeSpan.Zero || route.Count is < 2 or > 4)
            return null;
        if (route[0].InputId != request.SettlementId || route[^1].OutputId != request.SettlementId)
            return null;
        var seen = new HashSet<string>(StringComparer.Ordinal) { request.SettlementId };
        var stocks = new Dictionary<string, (string Input, string Output, DepthLevel Level)>(StringComparer.Ordinal);
        for (var i = 0; i < route.Count; i++)
        {
            var q = route[i];
            if (i < route.Count - 1 && !seen.Add(q.OutputId)) return null;
            if (!Metadata.Text(q.Id, q.InputId, q.OutputId, q.Source) || q.InputId == q.OutputId ||
                q.FetchedAt == default || q.FetchedAt > now || q.ObservedAt > q.FetchedAt ||
                q.Context != request.Context || !q.ConfirmedByUser || q.ObservedAt is null ||
                q.ObservedAt > now || now - q.ObservedAt >= request.MaximumAge ||
                (i > 0 && route[i - 1].OutputId != q.InputId) || q.Levels.Count == 0 || q.FeePerBatch < 0)
                return null;
            foreach (var level in q.Levels)
            {
                if (!Metadata.Text(level.StockId) || level.Rate.InputUnits <= 0 || level.Rate.OutputUnits <= 0 || level.AvailableBatches is null or < 0)
                    return null;
                var definition = (q.InputId, q.OutputId, level);
                if (stocks.TryGetValue(level.StockId, out var existing) && existing != definition) return null;
                stocks[level.StockId] = definition;
            }
        }
        var steps = new List<RouteStep>();
        var remainders = new Dictionary<string, long>(StringComparer.Ordinal);
        var available = sizingBudget;
        decimal capital = 0;
        decimal? totalGold = 0;
        var consumed = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var quote in route)
        {
            long input = 0, output = 0;
            decimal fee = 0;
            foreach (var level in quote.Levels)
            {
                var used = consumed.GetValueOrDefault(level.StockId);
                var batches = Math.Min((available - input) / level.Rate.InputUnits,
                    Math.Max(0, (level.AvailableBatches ?? 0) - used));
                input = checked(input + batches * level.Rate.InputUnits);
                output = checked(output + batches * level.Rate.OutputUnits);
                consumed[level.StockId] = checked(used + batches);
                fee += batches * quote.FeePerBatch;
            }
            if (output == 0) return null;
            var remainder = available - input + (steps.Count == 0 ? request.CapitalBudget - sizingBudget : 0);
            if (steps.Count == 0) capital = input;
            capital += fee;
            remainders[quote.InputId] = checked(remainders.GetValueOrDefault(quote.InputId) + remainder);
            var gold = (goldModel ?? new UnknownGoldCostModel()).Calculate(quote, input, output);
            var amount = gold.Quality != GoldQuality.Confirmed || gold.Amount is null or < 0 ||
                string.IsNullOrWhiteSpace(gold.Source) || gold.Patch != request.Context.Patch ||
                (gold.Game is not null && gold.Game != request.Context.Game) ||
                (gold.Realm is not null && gold.Realm != request.Context.Realm) ||
                (gold.CatalogVersion is not null && gold.CatalogVersion != request.Context.CatalogVersion) ||
                gold.CheckedAt is null || gold.CheckedAt == default(DateTimeOffset) || gold.CheckedAt > now
                ? null : gold.Amount;
            totalGold += amount;
            steps.Add(new(quote.Id, quote.InputId, quote.OutputId, input, output, remainder, fee, amount, quote.ObservedAt));
            available = output;
        }
        if (request.GoldBudget is { } limit && totalGold is null) return null;
        if (capital > request.CapitalBudget || (request.GoldBudget is { } budget && totalGold > budget))
        { tooLarge = true; return null; }
        var profit = available - capital;
        return new(string.Join("/", route.Select(q => q.Id)), steps, capital, available, profit,
            capital == 0 ? 0 : profit / capital * 100m, remainders, totalGold, totalGold > 0 ? profit / totalGold * 1000m : null,
            route.Min(q => q.ObservedAt)!.Value, "Manual — verify in game");
    }
}

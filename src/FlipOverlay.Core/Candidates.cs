namespace FlipOverlay.Core;

/// <summary>An aggregate estimate; source observation time may be unknown. Not an order or directed quote.</summary>
public sealed record AggregateValuation(MarketContext Context, string ItemId, decimal EstimatedUnitValue,
    string SourceId, DateTimeOffset? SourcePeriodEnd, DateTimeOffset FetchedAt);

/// <summary>One directed NPC conversion, not a reversible recipe.</summary>
public sealed record RecipeEdge(string Id, string InputId, string OutputId, BatchRate Rate,
    string Patch, string Source, DateTimeOffset CheckedAt, string Npc, string Condition,
    string? Game = null, string? Realm = null, string? CatalogVersion = null, string? Act = null);

public interface IRecipeCatalog
{
    IReadOnlyList<RecipeEdge> GetRecipes(MarketContext context);
}
public sealed class EmptyRecipeCatalog : IRecipeCatalog
{
    public IReadOnlyList<RecipeEdge> GetRecipes(MarketContext context) => [];
}
public interface IGameProfile
{
    string Game { get; }
    IRecipeCatalog Recipes { get; }
    IGoldCostModel GoldCosts { get; }
}

/// <summary>Budget and figures are in SettlementId; valuation requires a controlled item-ID mapping.</summary>
public sealed record CandidateRequest(MarketContext Context, string SettlementId, long CapitalBudget);
public sealed record EstimatedCandidate(string Id, IReadOnlyList<string> RecipeIds, string InputId, string OutputId,
    decimal CapitalRequired, decimal EstimatedRevenue, decimal EstimatedProfit, decimal EstimatedRoiPercent,
    IReadOnlyDictionary<string, long> Remainders, DateTimeOffset? SourcePeriodEnd, DateTimeOffset FetchedAt)
{
    public string Quality => "Estimated — verify in game";
    public decimal? Bid => null;
    public decimal? Ask => null;
    public long? Depth => null;
    public decimal? Gold => null;
}

/// <summary>Conservative candidate screening: charges for all purchased input, never credits unspent leftovers.</summary>
public sealed class CandidateEngine
{
    public IReadOnlyList<EstimatedCandidate> Evaluate(CandidateRequest request,
        IEnumerable<AggregateValuation> valuations, IEnumerable<RecipeEdge> recipes, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        if (!Metadata.Valid(request.Context) || request.CapitalBudget <= 0 || string.IsNullOrWhiteSpace(request.SettlementId)) return [];
        var prices = valuations.Where(x => x.Context == request.Context && x.EstimatedUnitValue > 0 &&
            Metadata.Text(x.ItemId, x.SourceId) && x.FetchedAt != default && x.FetchedAt <= currentTime &&
            (x.SourcePeriodEnd is null || (x.SourcePeriodEnd != default(DateTimeOffset) && x.SourcePeriodEnd <= x.FetchedAt)) &&
            x.Context.MarketKind == request.Context.MarketKind && x.ItemId != request.SettlementId)
            .GroupBy(x => (x.SourceId, x.ItemId))
            // Never arbitrarily choose one of several discordant estimates for the same instrument.
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        // Unit identity is arithmetic, not a market quote: 1 settlement unit = 1 settlement unit.
        foreach (var source in prices.Values.GroupBy(x => x.SourceId).ToArray())
            prices[(source.Key, request.SettlementId)] = new AggregateValuation(request.Context,
                request.SettlementId, 1m, source.Key, null, source.Max(x => x.FetchedAt));
        var valid = recipes.Where(r => r.Patch == request.Context.Patch && r.Rate.InputUnits > 0 &&
            r.Rate.OutputUnits > 0 && !string.IsNullOrWhiteSpace(r.Source) && !string.IsNullOrWhiteSpace(r.Npc) &&
            Metadata.Text(r.Id, r.InputId, r.OutputId, r.Condition) && r.InputId != r.OutputId &&
            r.CheckedAt != default && r.CheckedAt <= currentTime &&
            (r.Game is null || r.Game == request.Context.Game) &&
            (r.Realm is null || r.Realm == request.Context.Realm) &&
            (r.CatalogVersion is null || r.CatalogVersion == request.Context.CatalogVersion))
            .ToArray();
        var byInput = valid.GroupBy(r => r.InputId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var results = new List<EstimatedCandidate>();
        foreach (var start in prices.Values)
        {
            decimal amount;
            try { amount = decimal.Floor(request.CapitalBudget / start.EstimatedUnitValue); }
            catch (OverflowException) { continue; }
            if (amount <= 0 || amount > long.MaxValue) continue;
            Walk(start, start.ItemId, (long)amount, checked((long)amount) * start.EstimatedUnitValue,
                new List<RecipeEdge>(), new Dictionary<string, long>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal) { start.ItemId });
        }
        return results.OrderByDescending(x => x.EstimatedProfit).ThenBy(x => x.Id, StringComparer.Ordinal).Take(5000).ToArray();

        void Walk(AggregateValuation start, string input, long available, decimal cost,
            List<RecipeEdge> path, Dictionary<string, long> oldRemainders, HashSet<string> seen)
        {
            if (path.Count == 2 || results.Count >= 5000 || !byInput.TryGetValue(input, out var edges)) return;
            foreach (var edge in edges)
            {
                if (seen.Contains(edge.OutputId)) continue;
                BatchFill fill;
                try { fill = edge.Rate.Fill(available); }
                catch (OverflowException) { continue; }
                if (fill.Output == 0) continue;
                var actualCost = path.Count == 0 && start.ItemId == request.SettlementId ? fill.Input : cost;
                var remainder = new Dictionary<string, long>(oldRemainders, StringComparer.Ordinal);
                if (fill.Remainder > 0) remainder[input] = fill.Remainder;
                var nextPath = new List<RecipeEdge>(path) { edge };
                if (prices.TryGetValue((start.SourceId, edge.OutputId), out var sale))
                {
                    decimal revenue;
                    try { revenue = checked(fill.Output * sale.EstimatedUnitValue); }
                    catch (OverflowException) { continue; }
                    var profit = revenue - actualCost;
                    var period = start.ItemId == request.SettlementId ? sale.SourcePeriodEnd :
                        sale.ItemId == request.SettlementId ? start.SourcePeriodEnd :
                        start.SourcePeriodEnd is { } a && sale.SourcePeriodEnd is { } b ?
                        (a < b ? a : b) : (DateTimeOffset?)null;
                    try
                    {
                        results.Add(new EstimatedCandidate(start.SourceId + ":" + start.ItemId + ":" + string.Join("/", nextPath.Select(x => x.Id)),
                        nextPath.Select(x => x.Id).ToArray(), start.ItemId, edge.OutputId,
                        actualCost, revenue, profit, profit / actualCost * 100m, remainder, period,
                        start.FetchedAt > sale.FetchedAt ? start.FetchedAt : sale.FetchedAt));
                    }
                    catch (OverflowException) { continue; }
                }
                if (nextPath.Count < 2)
                {
                    var nextSeen = new HashSet<string>(seen, StringComparer.Ordinal) { edge.OutputId };
                    Walk(start, edge.OutputId, fill.Output, actualCost, nextPath, remainder, nextSeen);
                }
            }
        }
    }
}

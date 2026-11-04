namespace FlipOverlay.Core;

public enum GoldQuality { Unknown, Estimated, Confirmed }
public sealed record GoldCost(decimal? Amount, GoldQuality Quality, string Source, string Patch, DateTimeOffset? CheckedAt,
    string? Game = null, string? Realm = null, string? CatalogVersion = null);
/// <summary>Pure deterministic model; confirmed charges must be nonnegative and nondecreasing
/// with increasing received/input amounts. Required for budget-constrained batch sizing.
/// Estimated/unknown costs cannot satisfy a hard gold budget.</summary>
public interface IGoldCostModel
{
    GoldCost Calculate(ManualObservation observation, long input, long output);
}
public sealed class UnknownGoldCostModel : IGoldCostModel
{
    public GoldCost Calculate(ManualObservation observation, long input, long output) =>
        new(null, GoldQuality.Unknown, "No verified gold model", observation.Context.Patch, null);
}

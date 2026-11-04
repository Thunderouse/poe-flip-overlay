namespace FlipOverlay.App;

public enum WorkspaceMode { Candidates, Observed }
public sealed record LeagueDto(string Id, string Name) { public override string ToString() => Name; }
public sealed record WorkspaceRequest(string LeagueId, WorkspaceMode Mode, string SettlementCurrency = "", long CapitalBudget = 1000,
    decimal? MaximumGold = null, double MaximumAgeSeconds = 120, bool ManualRefresh = true);
public sealed record WorkspaceSnapshot(IReadOnlyList<ResultDto> Rows, string Status, string Source = "Unknown",
    DateTimeOffset? SourceObservedAt = null, DateTimeOffset? FetchedAt = null, DateTimeOffset? NextRefreshAt = null,
    string ExpectedCadence = "Unknown", string Market = "NPC Currency Exchange");
public sealed record WorkspaceSettings(bool ListMode = false, double Opacity = 1, string SettlementCurrency = "chaos",
    long CapitalBudget = 1000, decimal? MaximumGold = null, double MaximumAgeSeconds = 120, bool PauseRefresh = false);
public sealed record ManualEntry(string LeagueId, string GiveCurrency, decimal GiveQuantity, string ReceiveCurrency,
    decimal ReceiveQuantity, decimal? AvailableQuantity, decimal? Gold, DateTimeOffset? ObservedAt);
/// <summary>Adapters must keep aggregate candidates separate from manual observations; null is unknown.</summary>
public interface IWorkspaceBackend
{
    Task<IReadOnlyList<LeagueDto>> GetLeaguesAsync(CancellationToken cancellationToken);
    Task<WorkspaceSnapshot> RefreshAsync(WorkspaceRequest request, CancellationToken cancellationToken);
    Task SubmitManualAsync(ManualEntry entry, CancellationToken cancellationToken);
    Task ExportAsync(string path, WorkspaceRequest request, IReadOnlyList<ResultDto> visibleRows, CancellationToken cancellationToken);
    Task<WorkspaceSettings> LoadSettingsAsync(CancellationToken cancellationToken);
    Task SaveSettingsAsync(WorkspaceSettings settings, CancellationToken cancellationToken);
}
public sealed class UnavailableWorkspaceBackend : IWorkspaceBackend
{
    public Task<IReadOnlyList<LeagueDto>> GetLeaguesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LeagueDto>>([]);
    public Task<WorkspaceSnapshot> RefreshAsync(WorkspaceRequest request, CancellationToken cancellationToken) => Task.FromResult(new WorkspaceSnapshot([], "Source not configured. No market data available."));
    public Task SubmitManualAsync(ManualEntry entry, CancellationToken cancellationToken) => Task.FromException(new InvalidOperationException("Manual storage not configured; nothing saved."));
    public Task ExportAsync(string path, WorkspaceRequest request, IReadOnlyList<ResultDto> visibleRows, CancellationToken cancellationToken) => Task.FromException(new InvalidOperationException("Export not configured; nothing written."));
    public Task<WorkspaceSettings> LoadSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new WorkspaceSettings());
    public Task SaveSettingsAsync(WorkspaceSettings settings, CancellationToken cancellationToken) => Task.FromException(new InvalidOperationException("Settings storage not configured."));
}

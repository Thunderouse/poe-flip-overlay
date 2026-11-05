using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace FlipOverlay.App;

public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly IWorkspaceBackend backend;
    private CancellationTokenSource context = new();
    private long generation;
    private ManualEntry? confirmedEntry;
    private readonly TimeProvider time;
    private readonly ITimer timer;
    private readonly Action<Action> dispatch;
    private bool disposed;
    private bool refreshing;
    private bool ticking;
    private ResultDto[] latestRows = [];
    private DateTimeOffset? nextRefresh;
    private DateTimeOffset nextAge;
    private double displayedMaximumAge = 120;
    public WorkspaceViewModel(IWorkspaceBackend backend) : this(backend, TimeProvider.System,
        action => Avalonia.Threading.Dispatcher.UIThread.Post(action))
    { }
    public WorkspaceViewModel(IWorkspaceBackend backend, TimeProvider time, Action<Action> dispatch)
    {
        this.backend = backend; this.time = time; this.dispatch = dispatch;
        timer = time.CreateTimer(_ => dispatch(Tick), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Projection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Projection.Frozen) && !ticking) Tick();
        };
        Projection.Rows.CollectionChanged += (_, _) => NotifyPresentation();
    }
    [ObservableProperty] private bool isVisible = true;
    [ObservableProperty] private bool pauseRefresh;
    [ObservableProperty] private string settlementCurrency = "chaos";
    [ObservableProperty] private string capitalBudget = "1000";
    [ObservableProperty] private string maximumGold = "";
    [ObservableProperty] private string maximumAgeSeconds = "120";
    partial void OnIsVisibleChanged(bool value) { nextAge = time.GetUtcNow().AddSeconds(1); Schedule(); }
    partial void OnPauseRefreshChanged(bool value) => Schedule();
    partial void OnSettlementCurrencyChanged(string value) => ResetContext();
    partial void OnCapitalBudgetChanged(string value) => ResetContext();
    partial void OnMaximumGoldChanged(string value) => ResetContext();
    partial void OnMaximumAgeSecondsChanged(string value) => ResetContext();
    public ResultProjection Projection { get; } = new();
    public ManualEntryDraft Manual { get; } = new();
    public ObservableCollection<LeagueDto> Leagues { get; } = [];
    public WorkspaceMode[] Modes { get; } = Enum.GetValues<WorkspaceMode>();
    public SortMetric[] SortMetrics { get; } = Enum.GetValues<SortMetric>();
    [ObservableProperty] private LeagueDto? selectedLeague;
    [ObservableProperty] private WorkspaceMode mode;
    [ObservableProperty] private SortMetric sortMetric;
    [ObservableProperty] private bool descending = true;
    [ObservableProperty] private bool listMode;
    [ObservableProperty] private double windowOpacity = 1;
    [ObservableProperty] private string status = "No data loaded. Configure a source, then refresh.";
    [ObservableProperty] private string freshness = "Source: Unknown · Age unknown · Fetched: Unknown · Next refresh: Unknown";
    [ObservableProperty] private string market = "NPC Currency Exchange";
    [ObservableProperty] private string confirmation = "";
    [ObservableProperty] private bool confirmationReady;
    [ObservableProperty] private string exportPath = "";
    [ObservableProperty] private string catalogJson = "";
    [ObservableProperty] private string catalogPatch = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasLoaded;
    [ObservableProperty] private bool hasLoadError;
    [ObservableProperty] private bool isStale;
    [ObservableProperty] private bool sourceUnavailable;
    public bool ShowEmptyState => Projection.Rows.Count == 0;
    public bool ShowStateBanner => HasLoadError || IsStale;
    public string EmptyTitle => IsLoading ? "Loading market data" : HasLoadError ? "Could not load data" :
        SourceUnavailable ? "Data source unavailable" : IsStale ? "No current data" : HasLoaded ? "No matching routes" : "Ready when you are";
    public string EmptyDescription => IsLoading ? "Fetching the selected source. Existing filters are kept." :
        HasLoadError || SourceUnavailable || IsStale ? Status : HasLoaded ?
        "No routes meet the current filters. Adjust the budget or age limit, or add confirmed observations." :
        "Select a league and refresh. Candidates need a user-confirmed recipe catalog; manual routes need both exchange directions.";
    public string SortSummary => $"{SortMetric} {(Descending ? "↓" : "↑")}";
    partial void OnIsLoadingChanged(bool value) => NotifyPresentation();
    partial void OnHasLoadedChanged(bool value) => NotifyPresentation();
    partial void OnHasLoadErrorChanged(bool value) => NotifyPresentation();
    partial void OnIsStaleChanged(bool value) => NotifyPresentation();
    partial void OnSourceUnavailableChanged(bool value) => NotifyPresentation();
    partial void OnStatusChanged(string value) => NotifyPresentation();
    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(ShowEmptyState)); OnPropertyChanged(nameof(ShowStateBanner));
        OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyDescription));
    }
    [ObservableProperty] private bool catalogConfirmed;
    partial void OnCatalogJsonChanged(string value) => CatalogConfirmed = false;
    partial void OnCatalogPatchChanged(string value) => CatalogConfirmed = false;
    [RelayCommand]
    private async Task ImportCatalogAsync()
    {
        try
        {
            if (!CatalogConfirmed) throw new ArgumentException("Confirm recipe directions, access conditions and the current patch in game before importing.");
            if (backend is not IRecipeWorkspaceBackend recipes) throw new NotSupportedException("Recipe catalog import is not configured.");
            await recipes.ImportCatalogAsync(CatalogJson, CatalogPatch, CatalogConfirmed, context.Token);
            CatalogConfirmed = false;
            ResetContext();
            Status = "User-confirmed catalog saved. Refresh Candidates; estimates are not orders.";
        }
        catch (Exception e) when (e is not OperationCanceledException) { Status = "Catalog not imported: " + e.Message; }
    }
    public bool TableMode => !ListMode;
    partial void OnListModeChanged(bool value) => OnPropertyChanged(nameof(TableMode));
    partial void OnSelectedLeagueChanged(LeagueDto? value) { ResetContext(); Manual.LeagueId = value?.Id ?? ""; }
    partial void OnModeChanged(WorkspaceMode value) => ResetContext();
    partial void OnSortMetricChanged(SortMetric value) { Projection.SetSort(value, Descending); OnPropertyChanged(nameof(SortSummary)); }
    partial void OnDescendingChanged(bool value) { Projection.SetSort(SortMetric, value); OnPropertyChanged(nameof(SortSummary)); }
    private void ResetContext()
    {
        generation++; context.Cancel(); context.Dispose(); context = new();
        latestRows = []; nextRefresh = null; timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        HasLoaded = false; HasLoadError = false; SourceUnavailable = false; IsStale = false;
        Projection.Frozen = false; Projection.Replace([]); Freshness = "Source: Unknown · Age unknown · Fetched: Unknown · Next refresh: Unknown";
        Status = "Context changed; refresh to load data.";
    }
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var settings = await backend.LoadSettingsAsync(context.Token);
            ListMode = settings.ListMode; WindowOpacity = Math.Clamp(settings.Opacity, .4, 1);
            SettlementCurrency = settings.SettlementCurrency;
            CapitalBudget = settings.CapitalBudget.ToString(System.Globalization.CultureInfo.InvariantCulture);
            MaximumGold = settings.MaximumGold?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            MaximumAgeSeconds = settings.MaximumAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            PauseRefresh = settings.PauseRefresh;
            var leagues = await backend.GetLeaguesAsync(context.Token);
            foreach (var league in leagues) Leagues.Add(league);
            SelectedLeague = Leagues.FirstOrDefault();
        }
        catch (Exception e) when (e is not OperationCanceledException) { HasLoadError = true; Status = "Initialization failed: " + e.Message; }
        finally { IsLoading = false; }
    }
    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync(true);
    private async Task RefreshCoreAsync(bool manual)
    {
        if (disposed || refreshing) return;
        refreshing = true;
        IsLoading = true;
        var current = generation; var token = context.Token;
        try
        {
            var request = Request(manual);
            Status = "Refreshing…";
            var snapshot = await backend.RefreshAsync(request, token);
            if (current != generation || token.IsCancellationRequested) return;
            HasLoaded = true;
            HasLoadError = snapshot.DataState == WorkspaceDataState.Error;
            SourceUnavailable = snapshot.DataState == WorkspaceDataState.Unavailable;
            IsStale = snapshot.DataState == WorkspaceDataState.Stale;
            displayedMaximumAge = request.MaximumAgeSeconds;
            latestRows = snapshot.Rows.Where(row => Valid(row, time.GetUtcNow())).ToArray();
            nextRefresh = Mode == WorkspaceMode.Candidates ? snapshot.NextRefreshAt : null;
            if (nextRefresh is { } next && next <= time.GetUtcNow()) nextRefresh = time.GetUtcNow().AddSeconds(30);
            nextAge = time.GetUtcNow().AddSeconds(1);
            if (!HasLoadError && !IsStale) Projection.Replace(latestRows);
            Status = snapshot.Status; Market = snapshot.Market;
            Freshness = $"Source: {snapshot.Source} · Source observation: {Time(snapshot.SourceObservedAt)} · {(snapshot.SourceObservedAt is null ? "Age unknown" : "Age uses source observation, not fetch time")} · Fetched: {Time(snapshot.FetchedAt)} · Next refresh: {Time(snapshot.NextRefreshAt)} · Expected cadence: {snapshot.ExpectedCadence}";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (current == generation) { HasLoadError = true; Status = "Refresh failed: " + e.Message; } }
        finally
        {
            refreshing = false;
            IsLoading = false;
            if (current == generation && Mode == WorkspaceMode.Candidates && nextRefresh is null)
                nextRefresh = time.GetUtcNow().AddMinutes(5);
            Schedule();
        }
    }
    private WorkspaceRequest Request(bool manual = true)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (string.IsNullOrWhiteSpace(SettlementCurrency) ||
            !long.TryParse(CapitalBudget, System.Globalization.NumberStyles.None, culture, out var capital) || capital <= 0 ||
            !double.TryParse(MaximumAgeSeconds, System.Globalization.NumberStyles.AllowDecimalPoint, culture, out var age) || !double.IsFinite(age) || age < 1 || age > 86400)
            throw new ArgumentException("Enter a settlement currency, positive whole capital, and max age 1–86400 seconds.");
        decimal? gold = null;
        if (!string.IsNullOrWhiteSpace(MaximumGold))
        {
            if (!long.TryParse(MaximumGold, System.Globalization.NumberStyles.None, culture, out var amount)) throw new ArgumentException("Maximum gold must be a non-negative whole integer or blank.");
            gold = amount;
        }
        return new(SelectedLeague?.Id ?? "", Mode, SettlementCurrency.Trim(), capital, gold, age, manual);
    }
    private bool Valid(ResultDto row, DateTimeOffset now) => Mode != WorkspaceMode.Observed ||
        row.ObservedAt is { } at && at <= now && (now - at).TotalSeconds < displayedMaximumAge;
    private void Tick()
    {
        if (disposed || ticking) return;
        ticking = true;
        try
        {
            var now = time.GetUtcNow();
            var previousCount = latestRows.Length;
            latestRows = latestRows.Where(row => Valid(row, now)).ToArray();
            if (Mode == WorkspaceMode.Observed && latestRows.Length < previousCount) IsStale = true;
            if (Projection.Frozen && latestRows.Length != previousCount) Projection.Replace(latestRows);
            if (Projection.Rows.Any(row => !Valid(row.Data, now)))
            {
                var visible = Projection.Rows.Select(row => row.Data).Where(row => Valid(row, now)).ToArray();
                var frozen = Projection.Frozen;
                if (frozen) { Projection.Replace(visible); Projection.Frozen = false; }
                Projection.Replace(visible);
                Projection.Frozen = frozen;
                if (frozen) Projection.Replace(latestRows);
            }
            if (IsVisible && now >= nextAge)
            {
                foreach (var row in Projection.Rows)
                    if (row.Data.ObservedAt is { } at) row.Data = row.Data with { Age = (decimal)Math.Max(0, (now - at).TotalSeconds) };
                nextAge = now.AddSeconds(1);
            }
            if (!PauseRefresh && !refreshing && Mode == WorkspaceMode.Candidates && nextRefresh is { } next && now >= next)
            { nextRefresh = null; _ = RefreshCoreAsync(false); }
            Schedule();
        }
        finally { ticking = false; }
    }
    private void Schedule()
    {
        if (disposed) return;
        var now = time.GetUtcNow();
        var deadlines = new List<DateTimeOffset>();
        if (IsVisible && Projection.Rows.Count > 0) deadlines.Add(nextAge > now ? nextAge : now.AddSeconds(1));
        if (Mode == WorkspaceMode.Observed)
        {
            deadlines.AddRange(Projection.Rows.Select(row => row.Data.ObservedAt).Where(at => at.HasValue).Select(at => at!.Value.AddSeconds(displayedMaximumAge)));
            deadlines.AddRange(latestRows.Where(row => row.ObservedAt.HasValue).Select(row => row.ObservedAt!.Value.AddSeconds(displayedMaximumAge)));
        }
        if (!PauseRefresh && !refreshing && Mode == WorkspaceMode.Candidates && nextRefresh is { } next) deadlines.Add(next);
        var due = deadlines.Count == 0 ? Timeout.InfiniteTimeSpan : deadlines.Min() - now;
        if (due != Timeout.InfiniteTimeSpan && due <= TimeSpan.Zero) due = TimeSpan.FromMilliseconds(1);
        timer.Change(due, Timeout.InfiniteTimeSpan);
    }
    private static string Time(DateTimeOffset? value) => value?.ToString("u") ?? "Unknown";
    [RelayCommand]
    private void ReviewManual()
    {
        ConfirmationReady = false; confirmedEntry = null;
        try { confirmedEntry = Manual.ToEntry(); Confirmation = Manual.Confirmation; ConfirmationReady = true; }
        catch (ArgumentException e) { Confirmation = e.Message; }
    }
    [RelayCommand]
    private async Task ConfirmManualAsync()
    {
        if (!ConfirmationReady || confirmedEntry is null) return;
        try
        {
            if (Manual.ToEntry() != confirmedEntry) { ConfirmationReady = false; Confirmation = "Entry changed. Review again before confirming."; return; }
            await backend.SubmitManualAsync(confirmedEntry, context.Token);
            ConfirmationReady = false; Confirmation = "Observation saved. Verify in game; execution is not guaranteed.";
            await RefreshAsync();
        }
        catch (Exception e) { Status = "Manual entry not saved: " + e.Message; }
    }
    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ExportPath)) throw new ArgumentException("Enter an explicit export file path.");
            Tick();
            await backend.ExportAsync(ExportPath, Request(), Projection.Rows.Select(x => x.Data).ToArray(), context.Token);
            Status = "CSV exported to " + ExportPath;
        }
        catch (Exception e) { Status = "Export failed: " + e.Message; }
    }
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var request = Request();
            await backend.SaveSettingsAsync(new(ListMode, Math.Clamp(WindowOpacity, .4, 1), request.SettlementCurrency,
                request.CapitalBudget, request.MaximumGold, request.MaximumAgeSeconds, PauseRefresh), context.Token);
            Status = "Settings saved.";
        }
        catch (Exception e) { Status = "Settings not saved: " + e.Message; }
    }
    public void Dispose() { if (disposed) return; disposed = true; timer.Dispose(); context.Cancel(); context.Dispose(); (backend as IDisposable)?.Dispose(); }
}

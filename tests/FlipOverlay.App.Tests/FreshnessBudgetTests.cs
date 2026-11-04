using FlipOverlay.App;
using Xunit;
namespace FlipOverlay.App.Tests;

public sealed class FreshnessBudgetTests
{
    [Fact]
    public async Task Manual_and_background_refresh_never_overlap()
    {
        var clock = new Clock(); var backend = new Backend(clock);
        using var vm = new WorkspaceViewModel(backend, clock, action => action());
        await vm.RefreshCommand.ExecuteAsync(null);
        backend.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, backend.Calls);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, backend.Calls);
        backend.Hold.SetResult();
    }
    [Fact]
    public async Task Expired_pending_snapshot_cannot_resurrect_on_release()
    {
        var clock = new Clock(); var backend = new Backend(clock);
        using var vm = new WorkspaceViewModel(backend, clock, action => action());
        vm.Mode = WorkspaceMode.Observed;
        backend.ObservationOffset = 0;
        await vm.RefreshCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromSeconds(30));
        vm.Projection.Frozen = true; vm.IsVisible = false;
        backend.ObservationOffset = -30;
        await vm.RefreshCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromSeconds(60));
        vm.Projection.Frozen = false;
        Assert.Empty(vm.Projection.Rows);
    }
    [Fact]
    public async Task Hidden_frozen_rows_expire_without_network_or_age_repaints()
    {
        var clock = new Clock(); var backend = new Backend(clock);
        using var vm = new WorkspaceViewModel(backend, clock, action => action());
        vm.Mode = WorkspaceMode.Observed;
        await vm.RefreshCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.Projection.Rows);
        vm.Projection.Frozen = true;
        vm.IsVisible = false;
        var notifications = 0; row.PropertyChanged += (_, _) => notifications++;
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Single(vm.Projection.Rows); Assert.Equal(0, notifications);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(vm.Projection.Rows);
        vm.Projection.Frozen = false;
        Assert.Empty(vm.Projection.Rows); Assert.Equal(1, backend.Calls);
    }
    [Fact]
    public async Task Visible_age_changes_at_most_once_a_second()
    {
        var clock = new Clock(); var backend = new Backend(clock);
        using var vm = new WorkspaceViewModel(backend, clock, action => action());
        vm.Mode = WorkspaceMode.Observed;
        await vm.RefreshCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.Projection.Rows);
        clock.Advance(TimeSpan.FromMilliseconds(500)); Assert.Equal(0, row.Data.Age);
        clock.Advance(TimeSpan.FromMilliseconds(500)); Assert.Equal(1, row.Data.Age);
    }
    [Fact]
    public async Task Background_waits_for_cache_deadline_and_pause_prevents_refresh()
    {
        var clock = new Clock(); var backend = new Backend(clock);
        using var vm = new WorkspaceViewModel(backend, clock, action => action());
        await vm.RefreshCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromSeconds(9)); Assert.Equal(1, backend.Calls);
        clock.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(2, backend.Calls);
        vm.PauseRefresh = true;
        clock.Advance(TimeSpan.FromMinutes(10)); Assert.Equal(2, backend.Calls);
    }
    internal sealed class Backend(Clock clock) : IWorkspaceBackend
    {
        public int Calls;
        public int ObservationOffset;
        public TaskCompletionSource? Hold;
        public async Task<WorkspaceSnapshot> RefreshAsync(WorkspaceRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Hold is { } hold) await hold.Task.WaitAsync(cancellationToken);
            return new WorkspaceSnapshot([new("row", "a → b → a", ObservedAt: clock.Start.AddSeconds(ObservationOffset), Age: 0, Quality: "Manual")], "ok", NextRefreshAt: clock.GetUtcNow().AddSeconds(10));
        }
        public Task<IReadOnlyList<LeagueDto>> GetLeaguesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LeagueDto>>([]);
        public Task SubmitManualAsync(ManualEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task ExportAsync(string path, WorkspaceRequest request, IReadOnlyList<ResultDto> rows, CancellationToken ct) => Task.CompletedTask;
        public Task<WorkspaceSettings> LoadSettingsAsync(CancellationToken ct) => Task.FromResult(new WorkspaceSettings());
        public Task SaveSettingsAsync(WorkspaceSettings settings, CancellationToken ct) => Task.CompletedTask;
    }
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Start { get; } = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
        private DateTimeOffset now;
        private readonly List<Timer> timers = [];
        public Clock() => now = Start;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new Timer(this, callback, state); timers.Add(timer); timer.Change(dueTime, period); return timer; }
        public void Advance(TimeSpan amount)
        {
            var end = now + amount;
            while (timers.Where(t => t.Due <= end).OrderBy(t => t.Due).FirstOrDefault() is { } timer)
            { now = timer.Due; timer.Due = DateTimeOffset.MaxValue; timer.Callback(timer.State); }
            now = end;
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset Due = DateTimeOffset.MaxValue;
            public TimerCallback Callback = callback; public object? State = state;
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock.now + dueTime; return true; }
            public void Dispose() => Due = DateTimeOffset.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FlipOverlay.App;
using Xunit;

namespace FlipOverlay.App.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData("0.00000001", "1E-8")]
    [InlineData("-0.00000001", "-1E-8")]
    [InlineData("0", "0")]
    [InlineData("1000000.25", "1,000,000.25")]
    public void Small_values_keep_their_sign_and_never_round_to_zero(string input, string expected)
    {
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, ResultRow.DisplayNumber(value));
        Assert.Equal(value, new ResultRow(new("id", "route", Profit: value)).Data.Profit);
        Assert.Equal("Unknown", ResultRow.DisplayNumber(null));
    }

    [Fact]
    public void Close_to_tray_excludes_exit_and_operating_system_shutdown_and_guidance_is_one_shot()
    {
        var state = new WindowLifecycle();
        Assert.True(state.ShouldHideOnClose(trayAvailable: true, systemShutdown: false));
        Assert.False(state.ShouldHideOnClose(trayAvailable: false, systemShutdown: false));
        Assert.False(state.ShouldHideOnClose(trayAvailable: true, systemShutdown: true));
        Assert.True(state.TakeCloseGuidance());
        Assert.False(state.TakeCloseGuidance());
        state.RequestExit();
        Assert.False(state.ShouldHideOnClose(trayAvailable: true, systemShutdown: false));
    }

    [Fact]
    public async Task Refresh_errors_keep_previous_rows_and_are_not_presented_as_empty_success()
    {
        var source = new PresentationBackend();
        using var vm = new WorkspaceViewModel(source);
        Assert.Equal("Ready when you are", vm.EmptyTitle);
        await vm.RefreshCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.Projection.Rows);
        vm.Projection.Selected = row;
        source.State = WorkspaceDataState.Error;
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.True(vm.HasLoadError);
        Assert.True(vm.ShowStateBanner);
        Assert.False(vm.ShowEmptyState);
        Assert.Same(row, Assert.Single(vm.Projection.Rows));
        Assert.Same(row, vm.Projection.Selected);
    }

    [Fact]
    public async Task Loading_and_successful_empty_are_distinct_from_unavailable_and_stale()
    {
        var source = new PresentationBackend { Rows = [] };
        using var vm = new WorkspaceViewModel(source);
        vm.IsLoading = true;
        Assert.Equal("Loading market data", vm.EmptyTitle);
        vm.IsLoading = false;
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("No matching routes", vm.EmptyTitle);
        source.State = WorkspaceDataState.Unavailable;
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Data source unavailable", vm.EmptyTitle);
        source.State = WorkspaceDataState.Stale;
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("No current data", vm.EmptyTitle);
    }

    [AvaloniaFact]
    public void Unsupported_native_tray_does_not_enable_close_to_tray_or_leave_an_unattached_icon()
    {
        using var vm = new WorkspaceViewModel(new UnavailableWorkspaceBackend());
        var window = new MainWindow(vm);
        var desktop = new Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime();
        var original = desktop.ShutdownMode;
        Assert.Throws<PlatformNotSupportedException>(() => new TrayController(desktop, window));
        Assert.Null(TrayIcon.GetIcons(Avalonia.Application.Current!));
        Assert.Equal(original, desktop.ShutdownMode);
        window.Close();
    }

    [AvaloniaFact]
    public void Settings_action_expands_the_existing_settings_controls()
    {
        using var vm = new WorkspaceViewModel(new UnavailableWorkspaceBackend());
        var window = new MainWindow(vm);
        window.OpenSettings();
        Assert.True(window.FindControl<Expander>("SettingsExpander")!.IsExpanded);
        Assert.NotNull(window.Icon);
        window.Close();
    }

    private sealed class PresentationBackend : IWorkspaceBackend
    {
        public WorkspaceDataState State { get; set; }
        public IReadOnlyList<ResultDto> Rows { get; set; } = [new("fixture", "Offline test route", Profit: .00000001m)];
        public Task<WorkspaceSnapshot> RefreshAsync(WorkspaceRequest request, CancellationToken token) =>
            Task.FromResult(new WorkspaceSnapshot(State == WorkspaceDataState.Ready ? Rows : [], "Offline test source: " + State, DataState: State));
        public Task<IReadOnlyList<LeagueDto>> GetLeaguesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<LeagueDto>>([]);
        public Task<WorkspaceSettings> LoadSettingsAsync(CancellationToken token) => Task.FromResult(new WorkspaceSettings());
        public Task SaveSettingsAsync(WorkspaceSettings settings, CancellationToken token) => Task.CompletedTask;
        public Task SubmitManualAsync(ManualEntry entry, CancellationToken token) => Task.CompletedTask;
        public Task ExportAsync(string path, WorkspaceRequest request, IReadOnlyList<ResultDto> rows, CancellationToken token) => Task.CompletedTask;
    }
}

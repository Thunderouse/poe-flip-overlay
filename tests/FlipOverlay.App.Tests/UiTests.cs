using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FlipOverlay.App;
using Xunit;

namespace FlipOverlay.App.Tests;

public class UiTests
{
    [AvaloniaFact]
    public void Window_constructs_with_shared_projection_and_no_fabricated_rows()
    {
        using var vm = new WorkspaceViewModel(new UnavailableWorkspaceBackend());
        var window = new MainWindow(vm);
        window.Show();
        var table = window.FindControl<TableView>("ResultsTable")!;
        var list = window.FindControl<ListBox>("ResultsList")!;
        Assert.Same(vm.Projection.Rows, table.ItemsSource);
        Assert.Same(vm.Projection.Rows, list.ItemsSource);
        Assert.Empty(vm.Projection.Rows);
        Assert.Equal(9, table.Columns.Count);
        vm.ListMode = true;
        Assert.True(list.IsVisible);
        Assert.False(table.IsVisible);
        window.Close();
    }

    [Fact]
    public async Task Manual_review_and_confirmation_reaches_injected_backend()
    {
        var backend = new RecordingBackend();
        using var vm = new WorkspaceViewModel(backend);
        vm.Manual.LeagueId = "test-league";
        vm.Manual.GiveCurrency = "a";
        vm.Manual.ReceiveCurrency = "b";
        vm.Manual.GiveQuantity = "2";
        vm.Manual.ReceiveQuantity = "1";
        vm.Manual.TimestampUnknown = true;
        vm.ReviewManualCommand.Execute(null);
        Assert.True(vm.ConfirmationReady);
        await vm.ConfirmManualCommand.ExecuteAsync(null);
        Assert.NotNull(backend.Saved);
        Assert.Equal("test-league", backend.Saved.LeagueId);
        Assert.Null(backend.Saved.ObservedAt);
    }

    private sealed class RecordingBackend : IWorkspaceBackend
    {
        public ManualEntry? Saved { get; private set; }
        public Task<IReadOnlyList<LeagueDto>> GetLeaguesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LeagueDto>>([]);
        public Task<WorkspaceSnapshot> RefreshAsync(WorkspaceRequest request, CancellationToken ct) => Task.FromResult(new WorkspaceSnapshot([], "No paired observations."));
        public Task SubmitManualAsync(ManualEntry entry, CancellationToken ct) { Saved = entry; return Task.CompletedTask; }
        public Task ExportAsync(string path, WorkspaceRequest request, IReadOnlyList<ResultDto> rows, CancellationToken ct) => Task.CompletedTask;
        public Task<WorkspaceSettings> LoadSettingsAsync(CancellationToken ct) => Task.FromResult(new WorkspaceSettings());
        public Task SaveSettingsAsync(WorkspaceSettings settings, CancellationToken ct) => Task.CompletedTask;
    }
}

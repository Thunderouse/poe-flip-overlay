using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlipOverlay.App;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(FlipOverlay.App.Tests.VirtualizationApp))]

namespace FlipOverlay.App.Tests;

public static class VirtualizationApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class VirtualizationTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void Five_thousand_rows_realize_only_viewport_containers_and_share_selection_across_views()
    {
        // Explicit offline test data, never injected into the production startup/backend.
        using var vm = new WorkspaceViewModel(new UnavailableWorkspaceBackend());
        vm.Projection.Replace(Enumerable.Range(0, 5000).Select(i => new ResultDto(
            $"offline-{i:D4}", $"Offline fixture route {i}", Profit: i, Roi: i / 10m,
            Gold: i % 7 == 0 ? null : i, Source: "Offline virtualization fixture")));
        var window = new MainWindow(vm) { Width = 1080, Height = 730 };
        try
        {
            window.Show();
            Layout(window);
            var table = window.FindControl<TableView>("ResultsTable")!;
            var list = window.FindControl<ListBox>("ResultsList")!;
            Assert.Equal(5000, vm.Projection.Rows.Count);
            Assert.Same(vm.Projection.Rows, table.ItemsSource);
            Assert.Same(vm.Projection.Rows, list.ItemsSource);
            Assert.InRange(table.Bounds.Height, 1, 730);
            var tableRows = table.GetVisualDescendants().OfType<TableViewRow>().ToArray();
            output.WriteLine($"Table: source=5000, realized={tableRows.Length}, viewport={table.Bounds}");
            Assert.InRange(tableRows.Length, 1, 100);
            Assert.Contains(tableRows.SelectMany(row => row.GetVisualDescendants()).OfType<TextBlock>(),
                text => text.Text?.StartsWith("Offline fixture route ", StringComparison.Ordinal) == true);
            var selected = vm.Projection.Rows[3];
            table.SelectedItem = selected;
            Layout(window);
            Assert.Same(selected, vm.Projection.Selected);

            vm.ListMode = true;
            Layout(window);
            Assert.False(table.IsVisible);
            Assert.True(list.IsVisible);
            Assert.Same(selected, list.SelectedItem);
            Assert.InRange(list.Bounds.Height, 1, 730);
            Assert.Contains(list.GetVisualDescendants(), x => x is VirtualizingStackPanel);
            var listRows = list.GetRealizedContainers().OfType<ListBoxItem>().ToArray();
            var visualListRows = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
            output.WriteLine($"List: source=5000, realized={listRows.Length}, visual containers={visualListRows}, viewport={list.Bounds}");
            Assert.InRange(listRows.Length, 1, 100);
            Assert.InRange(visualListRows, 1, 100);

            // Exercise a distant viewport, not only the first screen.
            list.ScrollIntoView(vm.Projection.Rows[4900]);
            Layout(window);
            Assert.InRange(list.GetRealizedContainers().Count(), 1, 100);
            Assert.NotNull(list.ContainerFromIndex(4900));
            list.SelectedItem = vm.Projection.Rows[4900];
            var distantSelection = vm.Projection.Selected;
            Assert.NotNull(distantSelection);
            vm.ListMode = false;
            Layout(window);
            Assert.Same(distantSelection, table.SelectedItem);
            Assert.Same(vm.Projection.Rows, table.ItemsSource);
            Assert.InRange(table.GetVisualDescendants().OfType<TableViewRow>().Count(), 1, 100);
            var tableScroll = table.GetVisualDescendants().OfType<ScrollViewer>().First();
            tableScroll.Offset = new Vector(0, tableScroll.Extent.Height);
            Layout(window);
            var distantRows = table.GetVisualDescendants().OfType<TableViewRow>().ToArray();
            output.WriteLine($"Table after scrolling: realized={distantRows.Length}");
            Assert.InRange(distantRows.Length, 1, 100);
            Assert.Contains(distantRows, row => row.DataContext is ResultRow r && r.Id == vm.Projection.Rows[^1].Id);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(SortMetric.Profit)]
    [InlineData(SortMetric.Roi)]
    [InlineData(SortMetric.Gold)]
    [InlineData(SortMetric.ProfitPerThousandGold)]
    [InlineData(SortMetric.Capital)]
    [InlineData(SortMetric.Age)]
    public void Every_numeric_sort_keeps_unknown_last_in_both_directions_and_reuses_rows(SortMetric metric)
    {
        var projection = new ResultProjection();
        ResultDto Row(string id, decimal? value) => new(id, id, value, value, value, value, value, value);
        projection.Replace([Row("unknown", null), Row("two", 2), Row("ten", 10)]);
        var selected = projection.Rows.Single(x => x.Id == "two");
        projection.Selected = selected;
        projection.SetSort(metric, false);
        Assert.Equal(new[] { "two", "ten", "unknown" }, projection.Rows.Select(x => x.Id));
        projection.SetSort(metric, true);
        Assert.Equal(new[] { "ten", "two", "unknown" }, projection.Rows.Select(x => x.Id));
        projection.Replace([Row("unknown", null), Row("two", 3), Row("ten", 11)]);
        Assert.Same(selected, projection.Selected);
        Assert.Same(selected, projection.Rows.Single(x => x.Id == "two"));
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}

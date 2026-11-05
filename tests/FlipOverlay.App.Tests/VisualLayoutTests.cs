using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlipOverlay.App;
using Xunit;

namespace FlipOverlay.App.Tests;

public sealed class VisualLayoutTests
{
    [AvaloniaTheory]
    [InlineData(1100, 1.0)]
    [InlineData(880, 1.25)]
    [InlineData(760, 1.5)]
    public void Table_layout_keeps_headers_and_numeric_cells_bounded_at_narrow_widths(int width, double scale)
    {
        using var vm = new WorkspaceViewModel(new UnavailableWorkspaceBackend()) { HasLoaded = true };
        // Explicit offline fixtures only. They never enter normal startup, data sources or persistence.
        vm.Projection.Replace([
            new("long", "Chaos Orb → Divine Orb → Chaos Orb · Очень длинное название валюты для проверки обрезки", Profit: 1234567.123456m, Roi: 23.1234m, Gold: 3500, Capital: 4500, Unit: "chaos", Source: "Offline visual fixture"),
            new("tiny", "Small nonzero value", Profit: .00000001m, Roi: .00000002m, Gold: 0, Unit: "divine"),
            new("negative", "Negative value", Profit: -24.75m, Roi: -5m, Gold: 1250, Unit: "chaos"),
            new("zero", "Zero is known", Profit: 0, Roi: 0, Gold: 0, Unit: "chaos"),
            new("unknown", "Unknown stays distinct", Unit: "divine")]);
        var window = new MainWindow(vm) { Width = width, Height = 740 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var table = window.FindControl<TableView>("ResultsTable")!;
            Assert.InRange(table.Bounds.Width, 1, width);
            Assert.True(table.Bounds.Height >= 150);
            Assert.Equal(9, table.Columns.Count);
            foreach (var row in table.GetVisualDescendants().OfType<TableViewRow>())
                Assert.InRange(row.Bounds.Height, 32, 40);
            var target = Environment.GetEnvironmentVariable("POE_VISUAL_OUTPUT");
            if (!string.IsNullOrWhiteSpace(target))
            {
                Directory.CreateDirectory(target);
                using var image = new RenderTargetBitmap(new PixelSize((int)(width * scale), (int)(740 * scale)), new Vector(96 * scale, 96 * scale));
                image.Render(window);
                image.Save(Path.Combine(target, $"table-{width}-{scale:0.##}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }
}

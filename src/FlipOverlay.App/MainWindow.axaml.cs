using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FlipOverlay.App;

public partial class MainWindow : Window
{
    public MainWindow() : this(new WorkspaceViewModel(new UnavailableWorkspaceBackend())) { }
    public MainWindow(WorkspaceViewModel viewModel)
    {
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        var clickThrough = this.FindControl<Button>("ClickThroughButton")!;
        var controlsHint = this.FindControl<TextBlock>("WindowControlsHint")!;
        clickThrough.IsVisible = OperatingSystem.IsWindows();
        Opacity = viewModel.WindowOpacity;
        viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(viewModel.WindowOpacity)) Opacity = viewModel.WindowOpacity; };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty)
                viewModel.IsVisible = IsVisible && WindowState != WindowState.Minimized;
            if (e.Property == WindowsOverlayBridge.IsAvailableProperty)
                clickThrough.IsEnabled = WindowsOverlayBridge.GetIsAvailable(this);
            if (e.Property == WindowsOverlayBridge.StatusProperty)
                controlsHint.Text = WindowsOverlayBridge.GetStatus(this);
            if (e.Property == WindowsOverlayBridge.IsInteractiveProperty)
                clickThrough.Content = WindowsOverlayBridge.GetIsInteractive(this) ? "Enable click-through" : "Click-through enabled · Use Mouse input shortcut to edit";
        };
        Closed += (_, _) => viewModel.Dispose();
    }
    private WorkspaceViewModel ViewModel => (WorkspaceViewModel)DataContext!;
    private void ClickThrough_Click(object? sender, RoutedEventArgs e) => WindowsOverlayBridge.SetIsInteractive(this, false);
    private void MostProfit_Click(object? sender, RoutedEventArgs e) { ViewModel.SortMetric = SortMetric.Profit; ViewModel.Descending = true; }
    private void LeastGold_Click(object? sender, RoutedEventArgs e) { ViewModel.SortMetric = SortMetric.Gold; ViewModel.Descending = false; }
    private void BestRatio_Click(object? sender, RoutedEventArgs e) { ViewModel.SortMetric = SortMetric.ProfitPerThousandGold; ViewModel.Descending = true; }
}

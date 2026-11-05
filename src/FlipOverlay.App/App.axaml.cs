using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace FlipOverlay.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IWorkspaceBackend backend;
            string? startupError = null;
            try { backend = Program.BackendFactory(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                backend = new UnavailableWorkspaceBackend();
                startupError = "Data directory unavailable. No fallback was used: " + e.Message;
            }
            var vm = new WorkspaceViewModel(backend);
            var window = new MainWindow(vm);
            if (startupError is not null) { vm.HasLoadError = true; vm.Status = startupError; }
            desktop.MainWindow = window;
            TrayController? tray = null;
            if (OperatingSystem.IsWindows() && !Program.UiSmoke)
            {
                try { tray = new TrayController(desktop, window); }
                catch (Exception e) { vm.Status = "Tray unavailable; closing this window exits. " + e.Message; }
            }
            // Register close-to-tray before the adapter's Closing cleanup observes cancellation.
            // Both are attached before Show; no Windows adapter is constructed on other platforms.
            var overlay = Program.UiSmoke ? null : WindowsOverlayBridge.Attach(window, vm);
            Program.Instance?.SetActivationHandler(() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (tray is not null) tray.ShowForInteraction();
                else { window.Show(); window.Activate(); }
            }));
            desktop.Exit += (_, _) => { tray?.Dispose(); overlay?.Dispose(); vm.Dispose(); };
            window.Opened += async (_, _) =>
            {
                Program.SmokeOpened = true;
                try { await vm.InitializeAsync(); }
                catch (Exception e) { vm.Status = "Initialization failed: " + e.Message; }
                if (Program.UiSmoke) window.Close();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

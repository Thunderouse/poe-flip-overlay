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
            // Attach before Show; never construct the Windows adapter on other operating systems.
            var overlay = Program.UiSmoke ? null : WindowsOverlayBridge.Attach(window, vm);
            if (startupError is not null) vm.Status = startupError;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => { overlay?.Dispose(); vm.Dispose(); };
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

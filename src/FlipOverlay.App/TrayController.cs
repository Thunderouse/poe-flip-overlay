using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace FlipOverlay.App;

/// <summary>Owned by App until explicit exit. Avalonia's Win32 implementation re-adds the icon on TaskbarCreated.</summary>
public sealed class TrayController : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly MainWindow window;
    private readonly WindowLifecycle lifecycle = new();
    private readonly NativeMenuItem toggle;
    private readonly TrayIcon icon;
    private readonly Application application;
    private bool disposed;
    private bool closeGuidanceOpen;

    public TrayController(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        this.desktop = desktop;
        this.window = window;
        application = Application.Current ?? throw new InvalidOperationException("Application not initialized.");
        toggle = new NativeMenuItem("Hide overlay");
        toggle.Click += (_, _) => Toggle();
        var settings = new NativeMenuItem("Settings");
        settings.Click += (_, _) => { ShowForInteraction(); window.OpenSettings(); };
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => Exit();
        var menu = new NativeMenu();
        menu.Items.Add(toggle);
        menu.Items.Add(settings);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);
        using var resource = AssetLoader.Open(new Uri("avares://FlipOverlay.App/Assets/app.ico"));
        icon = new TrayIcon { Icon = new WindowIcon(resource), ToolTipText = "POE Flip Overlay", Menu = menu, IsVisible = true };
        icon.Clicked += (_, _) => Toggle();
        // Construction alone does not create a native icon in Avalonia. Attachment does.
        var icons = TrayIcon.GetIcons(application);
        if (icons is null)
        {
            icons = new TrayIcons();
            TrayIcon.SetIcons(application, icons);
        }
        try
        {
            icons.Add(icon);
            if (icon.NativeMenuExporter is null) throw new PlatformNotSupportedException("Native notification-area menu is unavailable.");
        }
        catch
        {
            icons.Remove(icon);
            if (icons.Count == 0) TrayIcon.SetIcons(application, null);
            icon.Dispose();
            throw;
        }
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Closing += OnClosing;
        window.PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
            toggle.Header = window.IsVisible && window.WindowState != WindowState.Minimized ? "Hide overlay" : "Show overlay";
    }

    public void Toggle()
    {
        if (window.IsVisible && window.WindowState != WindowState.Minimized) window.Hide();
        else ShowForInteraction();
    }

    public void ShowForInteraction()
    {
        WindowsOverlayBridge.SetIsInteractive(window, true);
        window.ShowActivated = true;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate(); // Tray/menu/second launch are explicit requests to interact, never background refresh.
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var systemShutdown = e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown;
        if (!lifecycle.ShouldHideOnClose(icon.IsVisible, systemShutdown)) return;
        e.Cancel = true;
        if (closeGuidanceOpen) return;
        if (lifecycle.TakeCloseGuidance())
        {
            closeGuidanceOpen = true;
            var explanation = new Window
            {
                Title = "POE Flip Overlay",
                Width = 390,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = window.Icon
            };
            var ok = new Button { Content = "Got it — hide to tray", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            ok.Click += (_, _) => explanation.Close();
            explanation.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Closing the window keeps the overlay running in the Windows notification area. Look under hidden icons if needed. Click its icon to reopen; choose Exit in its menu to quit.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    ok
                }
            };
            try { await explanation.ShowDialog(window); }
            finally { closeGuidanceOpen = false; }
        }
        if (!lifecycle.ExitRequested) window.Hide();
    }

    public void Exit()
    {
        lifecycle.RequestExit();
        Dispose();
        desktop.Shutdown();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.Closing -= OnClosing;
        window.PropertyChanged -= OnWindowPropertyChanged;
        icon.IsVisible = false;
        var icons = TrayIcon.GetIcons(application);
        icons?.Remove(icon);
        if (icons?.Count == 0) TrayIcon.SetIcons(application, null);
        icon.Dispose();
    }
}

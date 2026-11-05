using Avalonia;
using Avalonia.Controls;
using FlipOverlay.Platform.Abstractions;
using FlipOverlay.Platform.Windows;

namespace FlipOverlay.App;

/// <summary>Own-window only. Attach on the UI thread before the first Show.</summary>
public sealed class WindowsOverlayBridge : IDisposable
{
    public static readonly AttachedProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.RegisterAttached<Window, bool>("IsInteractive", typeof(WindowsOverlayBridge), defaultValue: true);
    public static readonly AttachedProperty<bool> IsAvailableProperty =
        AvaloniaProperty.RegisterAttached<Window, bool>("IsAvailable", typeof(WindowsOverlayBridge));
    public static readonly AttachedProperty<string> StatusProperty =
        AvaloniaProperty.RegisterAttached<Window, string>("Status", typeof(WindowsOverlayBridge),
            defaultValue: "Companion window only; platform overlay unavailable.");
    private static readonly AttachedProperty<WindowsOverlayBridge?> InstanceProperty =
        AvaloniaProperty.RegisterAttached<Window, WindowsOverlayBridge?>("Instance", typeof(WindowsOverlayBridge));

    private readonly Window window;
    private readonly WorkspaceViewModel viewModel;
    private readonly Win32Properties.CustomWndProcHookCallback hook;
    private readonly bool originalTopmost;
    private readonly bool originalShowActivated;
    private readonly bool originalShowInTaskbar;
    private readonly WindowDecorations originalDecorations;
    private WindowsPlatformService? platform;
    private WindowsOverlaySession? session;
    private OverlayHotkeys? bindings;
    private nint ownHandle;
    private bool disposed;
    private bool syncingMode;

    static WindowsOverlayBridge()
    {
        IsInteractiveProperty.Changed.AddClassHandler<Window>((window, _) =>
        {
            var bridge = window.GetValue(InstanceProperty);
            if (bridge is null || bridge.syncingMode || bridge.session is null) return;
            try { bridge.session.SetMode(GetIsInteractive(window) ? OverlayMode.Interactive : OverlayMode.Passive); }
            catch (Exception e) { bridge.Fail(e); }
        });
    }

    public static bool GetIsInteractive(Window window) => window.GetValue(IsInteractiveProperty);
    public static void SetIsInteractive(Window window, bool value) => window.SetValue(IsInteractiveProperty, value);
    public static bool GetIsAvailable(Window window) => window.GetValue(IsAvailableProperty);
    public static string GetStatus(Window window) => window.GetValue(StatusProperty);

    /// <summary>No Windows adapter is constructed, or window accessed, on other operating systems.</summary>
    public static WindowsOverlayBridge? Attach(Window window, WorkspaceViewModel viewModel)
    {
        if (!OperatingSystem.IsWindows()) return null;
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewModel);
        if (window.GetValue(InstanceProperty) is { } existing) return existing;
        try { return new WindowsOverlayBridge(window, viewModel); }
        catch (Exception e)
        {
            var status = "Windows overlay unavailable; normal interactive companion retained. " + e.Message;
            window.SetValue(StatusProperty, status);
            viewModel.Status = status;
            return null;
        }
    }

    private WindowsOverlayBridge(Window window, WorkspaceViewModel viewModel)
    {
        this.window = window;
        this.viewModel = viewModel;
        originalTopmost = window.Topmost;
        originalShowActivated = window.ShowActivated;
        originalShowInTaskbar = window.ShowInTaskbar;
        originalDecorations = window.WindowDecorations;
        hook = OnWindowMessage;
        try
        {
            window.SetValue(InstanceProperty, this);
            Win32Properties.AddWndProcHookCallback(window, hook);
            window.Opened += OnOpened;
            window.Closing += OnClosing;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnOpened(object? sender, EventArgs args)
    {
        if (disposed || platform is not null) return;
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.Handle == 0 || handle.HandleDescriptor != "HWND")
                throw new InvalidOperationException("Avalonia did not provide the application's own HWND.");
            bindings = OverlayHotkeys.ReadEnvironment(Environment.GetEnvironmentVariable);
            ownHandle = handle.Handle;
            platform = new WindowsPlatformService(ownHandle);
            session = new WindowsOverlaySession(platform, platform, bindings, ToggleVisibility, Refresh, ModeChanged);
            window.Topmost = true;
            window.SetValue(IsAvailableProperty, true);
            UpdateStatus();
        }
        catch (Exception e) { Fail(e); }
    }

    private void ModeChanged(OverlayMode mode)
    {
        syncingMode = true;
        try
        {
            SetIsInteractive(window, mode == OverlayMode.Interactive);
            window.ShowActivated = mode == OverlayMode.Interactive;
            UpdateStatus();
        }
        finally { syncingMode = false; }
    }

    private string ModeText() => GetIsInteractive(window) ? "Interactive" : "Passive / click-through";
    private void UpdateStatus()
    {
        if (bindings is null) return;
        window.SetValue(StatusProperty, $"{ModeText()} · " +
            $"Show/hide: {Describe(bindings.ToggleVisibility)} · Mouse input: {Describe(bindings.ToggleInteractive)} · Refresh: {Describe(bindings.Refresh)}.");
    }
    private static string Describe(HotkeyGesture gesture) => $"{gesture.Modifiers}+{gesture.Key}";

    private void ToggleVisibility()
    {
        if (window.IsVisible) window.Hide();
        else window.Show(); // ShowActivated is false in passive mode.
    }

    private void Refresh()
    {
        if (viewModel.RefreshCommand.CanExecute(null)) viewModel.RefreshCommand.Execute(null);
    }

    private nint OnWindowMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (disposed || platform is null) return 0;
        try
        {
            if (RouteOwnWindowMessage(platform, ownHandle, hwnd, message, wParam)) handled = true;
        }
        catch (Exception e)
        {
            handled = true; // Never unwind managed exceptions through the native window procedure.
            Fail(e);
        }
        return 0;
    }

    /// <summary>Range-check before narrowing; never dispatch another window's messages.</summary>
    public static bool RouteOwnWindowMessage(WindowsPlatformService service, nint ownHandle, nint hwnd, uint message, nint wParam)
    {
        if (ownHandle == 0 || hwnd != ownHandle || message != 0x0312 || wParam is < 1 or > 0xBFFF) return false;
        return service.HandleWindowMessage((int)message, (int)wParam);
    }

    private void Fail(Exception error)
    {
        Dispose();
        var status = "Windows overlay disabled; normal interactive companion retained. " + error.Message;
        window.SetValue(StatusProperty, status);
        viewModel.Status = status;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (!args.Cancel) Dispose(); // Release registrations and restore styles while the HWND still exists.
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<string> errors = [];
        Attempt(() => session?.Dispose());
        window.Opened -= OnOpened;
        window.Closing -= OnClosing;
        Attempt(() => Win32Properties.RemoveWndProcHookCallback(window, hook));
        // Restore Avalonia state before restoring the exact native styles/topmost snapshot.
        Attempt(() => window.Topmost = originalTopmost);
        Attempt(() => window.ShowActivated = originalShowActivated);
        Attempt(() => window.ShowInTaskbar = originalShowInTaskbar);
        Attempt(() => window.WindowDecorations = originalDecorations);
        Attempt(() => platform?.Dispose());
        window.SetValue(IsAvailableProperty, false);
        window.ClearValue(InstanceProperty);
        syncingMode = true;
        try { SetIsInteractive(window, true); }
        finally { syncingMode = false; }
        if (errors.Count > 0)
        {
            var status = "Windows overlay cleanup reported: " + string.Join("; ", errors);
            window.SetValue(StatusProperty, status);
            viewModel.Status = status;
        }

        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception e) { errors.Add(e.Message); }
        }
    }
}

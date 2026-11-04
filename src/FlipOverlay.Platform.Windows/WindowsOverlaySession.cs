using FlipOverlay.Platform.Abstractions;

namespace FlipOverlay.Platform.Windows;

/// <summary>UI-thread session. Registers recovery hotkeys before enabling passive mode.</summary>
public sealed class WindowsOverlaySession : IDisposable
{
    private readonly IOverlayWindowService overlay;
    private readonly IHotkeyService hotkeys;
    private readonly Action<OverlayMode> modeChanged;
    private readonly List<int> registrations = [];
    private OverlayMode mode = OverlayMode.Interactive;
    private bool disposed;

    public WindowsOverlaySession(IOverlayWindowService overlay, IHotkeyService hotkeys, OverlayHotkeys bindings,
        Action toggleVisibility, Action refresh, Action<OverlayMode> modeChanged)
    {
        this.overlay = overlay;
        this.hotkeys = hotkeys;
        this.modeChanged = modeChanged;
        try
        {
            registrations.Add(hotkeys.Register(bindings.ToggleVisibility, toggleVisibility));
            registrations.Add(hotkeys.Register(bindings.ToggleInteractive,
                () => SetMode(mode == OverlayMode.Passive ? OverlayMode.Interactive : OverlayMode.Passive)));
            registrations.Add(hotkeys.Register(bindings.Refresh, refresh));
            SetMode(OverlayMode.Passive);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetMode(OverlayMode value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        overlay.SetMode(value);
        mode = value;
        modeChanged(value);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception> errors = [];
        foreach (var id in registrations)
        {
            try { hotkeys.Unregister(id); }
            catch (Exception e) { errors.Add(e); }
        }
        registrations.Clear();
        if (errors.Count > 0) throw new AggregateException("Unable to release overlay hotkeys.", errors);
    }
}

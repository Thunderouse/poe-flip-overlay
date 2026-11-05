namespace FlipOverlay.App;

/// <summary>Window/tray state independent of platform handles; one-shot close guidance is never repeated.</summary>
public sealed class WindowLifecycle
{
    public bool ExitRequested { get; private set; }
    public bool CloseGuidanceShown { get; private set; }
    public bool ShouldHideOnClose(bool trayAvailable, bool systemShutdown) => trayAvailable && !ExitRequested && !systemShutdown;
    public bool TakeCloseGuidance()
    {
        if (CloseGuidanceShown) return false;
        CloseGuidanceShown = true;
        return true;
    }
    public void RequestExit() => ExitRequested = true;
}

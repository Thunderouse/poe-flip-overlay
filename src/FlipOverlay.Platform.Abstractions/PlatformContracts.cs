namespace FlipOverlay.Platform.Abstractions;

[Flags]
public enum PlatformCapabilities
{
    None = 0,
    OwnWindowOverlay = 1,
    ClickThrough = 2,
    NoActivate = 4,
    GlobalHotkeys = 8,
}

public enum OverlayMode { Passive, Interactive }

[Flags]
public enum HotkeyModifiers : uint { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

public enum HotkeyKey : uint { F8 = 0x77, F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B }

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, HotkeyKey Key);

public interface IOverlayWindowService
{
    PlatformCapabilities Capabilities { get; }
    void SetMode(OverlayMode mode);
}

public interface IHotkeyService
{
    PlatformCapabilities Capabilities { get; }
    int Register(HotkeyGesture gesture, Action callback);
    void Unregister(int id);
}

public interface IAppPaths
{
    string DataDirectory { get; }
    string CacheDirectory { get; }
    string LogsDirectory { get; }
}

public interface ICredentialStore
{
    string? Read(string key);
    void Write(string key, string value);
    void Delete(string key);
}

public interface IExternalLinkLauncher
{
    void Open(Uri address);
}

public sealed class HotkeyRegistrationException : InvalidOperationException
{
    public HotkeyRegistrationException(HotkeyGesture gesture)
        : base($"The operating system refused hotkey {gesture}; it may be in use by another application.") { }
}

/// <summary>Explicit unsupported companion implementation; never claims to be an overlay.</summary>
public sealed class UnsupportedCompanionService : IOverlayWindowService, IHotkeyService, IDisposable
{
    public PlatformCapabilities Capabilities => PlatformCapabilities.None;
    public void SetMode(OverlayMode mode) => throw new PlatformNotSupportedException("Own-window overlay is unavailable on this platform.");
    public int Register(HotkeyGesture gesture, Action callback) => throw new PlatformNotSupportedException("Global hotkeys are unavailable on this platform.");
    public void Unregister(int id) => throw new PlatformNotSupportedException("Global hotkeys are unavailable on this platform.");
    public void Dispose() { }
}

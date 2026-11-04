using System.ComponentModel;
using System.Runtime.InteropServices;
using FlipOverlay.Platform.Abstractions;

namespace FlipOverlay.Platform.Windows;

/// <summary>Bridge this interface only to the application's own Avalonia window handle.</summary>
public interface IWindowNativeApi
{
    int GetExtendedStyle(nint hwnd);
    void SetExtendedStyle(nint hwnd, int style);
    void SetWindowPosition(nint hwnd, nint insertAfter, uint flags);
    bool RegisterHotkey(nint hwnd, int id, uint modifiers, uint key);
    bool UnregisterHotkey(nint hwnd, int id);
}

public sealed class WindowsPlatformService : IOverlayWindowService, IHotkeyService, IDisposable
{
    private const int ToolWindow = 0x80;
    private const int Transparent = 0x20;
    private const int NoActivate = 0x08000000;
    private const int Layered = 0x80000;
    private const int HotkeyMessage = 0x0312;
    private readonly nint handle;
    private readonly IWindowNativeApi native;
    private readonly bool isWindows;
    private readonly Dictionary<int, Action> callbacks = new();
    private readonly int originalStyle;
    private bool changed;
    private bool disposed;
    private int nextId;

    public WindowsPlatformService(nint ownWindowHandle) : this(ownWindowHandle, new WindowsNativeApi(), OperatingSystem.IsWindows()) { }

    public WindowsPlatformService(nint ownWindowHandle, IWindowNativeApi native, bool isWindows)
    {
        if (ownWindowHandle == 0) throw new ArgumentException("An own-window handle is required.", nameof(ownWindowHandle));
        handle = ownWindowHandle;
        this.native = native ?? throw new ArgumentNullException(nameof(native));
        this.isWindows = isWindows;
        if (isWindows) originalStyle = native.GetExtendedStyle(handle);
    }

    public PlatformCapabilities Capabilities => isWindows ?
        PlatformCapabilities.OwnWindowOverlay | PlatformCapabilities.ClickThrough |
        PlatformCapabilities.NoActivate | PlatformCapabilities.GlobalHotkeys : PlatformCapabilities.None;

    public void SetMode(OverlayMode mode)
    {
        Check();
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var style = (originalStyle | ToolWindow) & ~(Transparent | NoActivate);
        if (mode == OverlayMode.Passive) style |= Transparent | NoActivate | Layered;
        native.SetExtendedStyle(handle, style);
        changed = true;
        UpdateFrame(topmost: true, noActivate: mode == OverlayMode.Passive);
    }

    public int Register(HotkeyGesture gesture, Action callback)
    {
        Check();
        ArgumentNullException.ThrowIfNull(callback);
        if (gesture.Modifiers == HotkeyModifiers.None ||
            (gesture.Modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win)) != 0 ||
            !Enum.IsDefined(gesture.Key) || gesture.Key == HotkeyKey.F12)
            throw new ArgumentException("Unsupported hotkey gesture.", nameof(gesture));
        var id = checked(++nextId);
        if (id > 0xBFFF) throw new InvalidOperationException("Too many hotkeys registered.");
        if (!native.RegisterHotkey(handle, id, (uint)gesture.Modifiers | 0x4000, (uint)gesture.Key))
            throw new HotkeyRegistrationException(gesture);
        callbacks.Add(id, callback);
        return id;
    }

    public bool HandleWindowMessage(int message, int wParam)
    {
        Check();
        if (message != HotkeyMessage || !callbacks.TryGetValue(wParam, out var callback)) return false;
        callback();
        return true;
    }

    public void Unregister(int id)
    {
        Check();
        if (!callbacks.ContainsKey(id)) return;
        if (!native.UnregisterHotkey(handle, id)) throw new Win32Exception("Unable to unregister own hotkey.");
        callbacks.Remove(id);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception> errors = [];
        foreach (var id in callbacks.Keys.ToArray())
        {
            Attempt(() =>
            {
                if (!native.UnregisterHotkey(handle, id)) throw new Win32Exception("Unable to unregister own hotkey.");
            });
        }
        callbacks.Clear();
        if (changed)
        {
            Attempt(() => native.SetExtendedStyle(handle, originalStyle));
            Attempt(() => UpdateFrame(topmost: (originalStyle & 0x8) != 0, noActivate: true));
        }
        if (errors.Count > 0) throw new AggregateException("Windows overlay cleanup failed.", errors);

        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception e) { errors.Add(e); }
        }
    }

    private void UpdateFrame(bool topmost, bool noActivate) =>
        native.SetWindowPosition(handle, topmost ? -1 : -2, 0x1u | 0x2u | 0x20u | (noActivate ? 0x10u : 0));

    private void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!isWindows) throw new PlatformNotSupportedException("Windows own-window overlay is unavailable on this platform.");
    }
}

internal sealed class WindowsNativeApi : IWindowNativeApi
{
    private const int ExStyleIndex = -20;
    public int GetExtendedStyle(nint hwnd) => unchecked((int)Native.GetWindowLongPtrW(hwnd, ExStyleIndex));
    public void SetExtendedStyle(nint hwnd, int style)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = Native.SetWindowLongPtrW(hwnd, ExStyleIndex, style);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    public void SetWindowPosition(nint hwnd, nint insertAfter, uint flags)
    {
        if (!Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, flags))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    public bool RegisterHotkey(nint hwnd, int id, uint modifiers, uint key) => Native.RegisterHotKey(hwnd, id, modifiers, key);
    public bool UnregisterHotkey(nint hwnd, int id) => Native.UnregisterHotKey(hwnd, id);
}

internal static partial class Native
{
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hwnd, int id);
}

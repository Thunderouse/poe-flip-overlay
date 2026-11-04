using Avalonia;
using Avalonia.Controls;
using FlipOverlay.App;
using FlipOverlay.Platform.Abstractions;
using FlipOverlay.Platform.Windows;
using Xunit;

namespace FlipOverlay.Platform.Tests;

public sealed class AppBridgeTests
{
    [Fact]
    public void Non_windows_attach_returns_without_touching_window_or_loading_user32()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Null(WindowsOverlayBridge.Attach(null!, null!));
    }

    [Fact]
    public void Compiled_callback_routes_only_own_window_registered_hotkey_without_truncating_parameters()
    {
        using var service = new WindowsPlatformService(42, new NativeFake(), isWindows: true);
        var count = 0;
        var id = service.Register(new(HotkeyModifiers.Control, HotkeyKey.F8), () => count++);
        Assert.False(WindowsOverlayBridge.RouteOwnWindowMessage(service, 42, 43, 0x312, id));
        Assert.False(WindowsOverlayBridge.RouteOwnWindowMessage(service, 42, 42, 0x100, id));
        Assert.False(WindowsOverlayBridge.RouteOwnWindowMessage(service, 42, 42, 0x312, (nint)((1L << 32) + id)));
        Assert.True(WindowsOverlayBridge.RouteOwnWindowMessage(service, 42, 42, 0x312, id));
        Assert.Equal(1, count);
        Assert.Equal(typeof(nint), typeof(Win32Properties.CustomWndProcHookCallback).GetMethod("Invoke")!.ReturnType);
    }

    private sealed class NativeFake : IWindowNativeApi
    {
        public int GetExtendedStyle(nint hwnd) => 0;
        public void SetExtendedStyle(nint hwnd, int style) { }
        public void SetWindowPosition(nint hwnd, nint insertAfter, uint flags) { }
        public bool RegisterHotkey(nint hwnd, int id, uint modifiers, uint key) => true;
        public bool UnregisterHotkey(nint hwnd, int id) => true;
    }
}

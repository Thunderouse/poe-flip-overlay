using FlipOverlay.Platform.Abstractions;
using FlipOverlay.Platform.Windows;
using Xunit;

namespace FlipOverlay.Platform.Tests;

public sealed class WindowsPlatformTests
{
    [Fact]
    public void Non_windows_reports_no_capabilities_and_never_calls_native_api()
    {
        var native = new FakeNative();
        using var platform = new WindowsPlatformService(42, native, isWindows: false);
        Assert.Equal(PlatformCapabilities.None, platform.Capabilities);
        Assert.Throws<PlatformNotSupportedException>(() => platform.SetMode(OverlayMode.Passive));
        Assert.Throws<PlatformNotSupportedException>(() => platform.Register(new(HotkeyModifiers.Control, HotkeyKey.F8), () => { }));
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public void Passive_and_interactive_modes_only_change_own_window_styles()
    {
        var native = new FakeNative { Style = 0x1000 };
        using var platform = new WindowsPlatformService(42, native, isWindows: true);
        platform.SetMode(OverlayMode.Passive);
        Assert.Equal((nint)42, native.Window);
        Assert.Equal(0x1000 | 0x20 | 0x8000000 | 0x80000, native.Style);
        Assert.Equal(1, native.FrameUpdates);
        platform.SetMode(OverlayMode.Interactive);
        Assert.Equal(0x1000, native.Style);
        Assert.Equal(2, native.FrameUpdates);
    }

    [Fact]
    public void Hotkey_dispatches_only_registered_id_and_unregister_stops_dispatch()
    {
        var native = new FakeNative();
        using var platform = new WindowsPlatformService(42, native, isWindows: true);
        var count = 0;
        var id = platform.Register(new(HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyKey.F8), () => count++);
        Assert.Equal((uint)0x4006, native.Modifiers);
        Assert.Equal((uint)0x77, native.Key);
        Assert.False(platform.HandleWindowMessage(0x0312, id + 1));
        Assert.False(platform.HandleWindowMessage(0x100, id));
        Assert.True(platform.HandleWindowMessage(0x0312, id));
        Assert.Equal(1, count);
        platform.Unregister(id);
        Assert.Equal(id, native.UnregisteredId);
        Assert.False(platform.HandleWindowMessage(0x0312, id));
    }

    [Fact]
    public void Failed_registration_has_no_callback_and_reports_conflict()
    {
        var native = new FakeNative { RegisterSucceeds = false };
        using var platform = new WindowsPlatformService(42, native, isWindows: true);
        Assert.Throws<HotkeyRegistrationException>(() => platform.Register(new(HotkeyModifiers.Alt, HotkeyKey.F9), () => { }));
        Assert.False(platform.HandleWindowMessage(0x0312, 1));
    }

    [Fact]
    public void Disposal_unregisters_every_hotkey_and_rejects_use()
    {
        var native = new FakeNative();
        var platform = new WindowsPlatformService(42, native, isWindows: true);
        platform.Register(new(HotkeyModifiers.Control, HotkeyKey.F8), () => { });
        platform.Dispose();
        Assert.Equal(1, native.UnregisteredId);
        Assert.Throws<ObjectDisposedException>(() => platform.SetMode(OverlayMode.Passive));
    }

    [Fact]
    public void Interactive_clears_preexisting_passive_flags_and_dispose_restores_original_style()
    {
        const int original = 0x1000 | 0x20 | 0x08000000 | 0x80000;
        var native = new FakeNative { Style = original };
        var platform = new WindowsPlatformService(42, native, isWindows: true);
        platform.SetMode(OverlayMode.Interactive);
        Assert.Equal(0x1000 | 0x80000, native.Style);
        platform.Dispose();
        Assert.Equal(original, native.Style);
    }

    [Theory]
    [InlineData(0, -2)]
    [InlineData(8, -1)]
    public void Topmost_is_applied_without_nozorder_and_disposal_restores_original_state(int original, int restored)
    {
        var native = new FakeNative { Style = original };
        var platform = new WindowsPlatformService(42, native, isWindows: true);
        platform.SetMode(OverlayMode.Passive);
        Assert.Equal((nint)(-1), native.InsertAfter);
        Assert.Equal(0u, native.PositionFlags & 0x4u);
        Assert.NotEqual(0u, native.PositionFlags & 0x10u);
        platform.Dispose();
        Assert.Equal((nint)restored, native.InsertAfter);
        Assert.Equal(original, native.Style);
        Assert.NotEqual(0u, native.PositionFlags & 0x10u);
    }

    [Fact]
    public void Failed_unregister_retains_registration_for_retry_and_cleanup()
    {
        var native = new FakeNative { UnregisterSucceeds = false };
        using var platform = new WindowsPlatformService(42, native, isWindows: true);
        var called = false;
        var id = platform.Register(new(HotkeyModifiers.Control, HotkeyKey.F8), () => called = true);
        Assert.Throws<System.ComponentModel.Win32Exception>(() => platform.Unregister(id));
        Assert.True(platform.HandleWindowMessage(0x0312, id));
        Assert.True(called);
        native.UnregisterSucceeds = true;
        platform.Unregister(id);
        Assert.False(platform.HandleWindowMessage(0x0312, id));
    }

    [Fact]
    public void Dispose_reports_unregister_errors_but_still_restores_styles_and_topmost()
    {
        var native = new FakeNative { Style = 0x1000 };
        var platform = new WindowsPlatformService(42, native, isWindows: true);
        platform.SetMode(OverlayMode.Passive);
        platform.Register(new(HotkeyModifiers.Control, HotkeyKey.F8), () => { });
        native.UnregisterSucceeds = false;
        Assert.Throws<AggregateException>(() => platform.Dispose());
        Assert.Equal(0x1000, native.Style);
        Assert.Equal((nint)(-2), native.InsertAfter);
        platform.Dispose();
    }

    private sealed class FakeNative : IWindowNativeApi
    {
        public int Style { get; set; }
        public nint Window { get; private set; }
        public int Calls { get; private set; }
        public int FrameUpdates { get; private set; }
        public nint InsertAfter { get; private set; }
        public uint PositionFlags { get; private set; }
        public uint Modifiers { get; private set; }
        public uint Key { get; private set; }
        public int UnregisteredId { get; private set; }
        public bool RegisterSucceeds { get; set; } = true;
        public bool UnregisterSucceeds { get; set; } = true;
        public int GetExtendedStyle(nint hwnd) { Calls++; Window = hwnd; return Style; }
        public void SetExtendedStyle(nint hwnd, int style) { Calls++; Window = hwnd; Style = style; }
        public void SetWindowPosition(nint hwnd, nint insertAfter, uint flags) { Calls++; Window = hwnd; FrameUpdates++; InsertAfter = insertAfter; PositionFlags = flags; }
        public bool RegisterHotkey(nint hwnd, int id, uint modifiers, uint key) { Calls++; Window = hwnd; Modifiers = modifiers; Key = key; return RegisterSucceeds; }
        public bool UnregisterHotkey(nint hwnd, int id) { Calls++; Window = hwnd; UnregisteredId = id; return UnregisterSucceeds; }
    }
}

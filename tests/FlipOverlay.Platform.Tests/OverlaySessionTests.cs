using FlipOverlay.Platform.Abstractions;
using FlipOverlay.Platform.Windows;
using Xunit;

namespace FlipOverlay.Platform.Tests;

public sealed class OverlaySessionTests
{
    [Fact]
    public void Session_starts_interactive_and_wires_explicit_passive_visibility_refresh_and_cleanup()
    {
        var platform = new FakePlatform();
        var toggles = 0;
        var refreshes = 0;
        using (var session = new WindowsOverlaySession(platform, platform, OverlayHotkeys.Default,
            () => toggles++, () => refreshes++, _ => { }))
        {
            Assert.Equal(OverlayMode.Interactive, platform.Mode);
            platform.Callbacks[1]();
            platform.Callbacks[2]();
            platform.Callbacks[3]();
            Assert.Equal(1, toggles);
            Assert.Equal(OverlayMode.Passive, platform.Mode);
            Assert.Equal(1, refreshes);
            session.SetMode(OverlayMode.Passive);
            Assert.Equal(OverlayMode.Passive, platform.Mode);
        }
        Assert.Empty(platform.Callbacks);
    }

    [Fact]
    public void Conflict_rolls_back_registrations_before_any_passive_transition()
    {
        var platform = new FakePlatform { FailAt = 2 };
        Assert.Throws<HotkeyRegistrationException>(() => new WindowsOverlaySession(platform, platform,
            OverlayHotkeys.Default, () => { }, () => { }, _ => { }));
        Assert.Empty(platform.Callbacks);
        Assert.Equal(OverlayMode.Interactive, platform.Mode);
    }

    [Fact]
    public void Configurable_bindings_accept_named_modifiers_and_reject_F12_and_duplicates()
    {
        var values = new Dictionary<string, string>
        {
            ["FLIP_OVERLAY_HOTKEY_VISIBILITY"] = "Alt+Shift+F11",
            ["FLIP_OVERLAY_HOTKEY_INTERACTIVE"] = "Control+F9",
        };
        var settings = OverlayHotkeys.ReadEnvironment(key => values.GetValueOrDefault(key));
        Assert.Equal(new HotkeyGesture(HotkeyModifiers.Alt | HotkeyModifiers.Shift, HotkeyKey.F11), settings.ToggleVisibility);
        Assert.Equal(OverlayHotkeys.Default.Refresh, settings.Refresh);
        values["FLIP_OVERLAY_HOTKEY_VISIBILITY"] = "Control+F12";
        Assert.Throws<ArgumentException>(() => OverlayHotkeys.ReadEnvironment(key => values.GetValueOrDefault(key)));
        values["FLIP_OVERLAY_HOTKEY_VISIBILITY"] = "Control+F9";
        Assert.Throws<ArgumentException>(() => OverlayHotkeys.ReadEnvironment(key => values.GetValueOrDefault(key)));
    }

    private sealed class FakePlatform : IOverlayWindowService, IHotkeyService
    {
        private int next;
        public int FailAt { get; init; }
        public Dictionary<int, Action> Callbacks { get; } = [];
        public OverlayMode Mode { get; private set; } = OverlayMode.Interactive;
        public PlatformCapabilities Capabilities => PlatformCapabilities.OwnWindowOverlay | PlatformCapabilities.GlobalHotkeys;
        public void SetMode(OverlayMode mode) => Mode = mode;
        public int Register(HotkeyGesture gesture, Action callback)
        {
            var id = ++next;
            if (id == FailAt) throw new HotkeyRegistrationException(gesture);
            Callbacks.Add(id, callback);
            return id;
        }
        public void Unregister(int id) => Callbacks.Remove(id);
    }
}

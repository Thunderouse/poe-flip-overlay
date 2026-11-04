namespace FlipOverlay.Platform.Abstractions;

/// <summary>Only actions on the companion application's own window and data.</summary>
public sealed record OverlayHotkeys(HotkeyGesture ToggleVisibility, HotkeyGesture ToggleInteractive, HotkeyGesture Refresh)
{
    public static OverlayHotkeys ReadEnvironment(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var settings = new OverlayHotkeys(
            Read("FLIP_OVERLAY_HOTKEY_VISIBILITY", Default.ToggleVisibility),
            Read("FLIP_OVERLAY_HOTKEY_INTERACTIVE", Default.ToggleInteractive),
            Read("FLIP_OVERLAY_HOTKEY_REFRESH", Default.Refresh));
        if (new[] { settings.ToggleVisibility, settings.ToggleInteractive, settings.Refresh }.Distinct().Count() != 3)
            throw new ArgumentException("Overlay hotkeys must be distinct.");
        return settings;

        HotkeyGesture Read(string name, HotkeyGesture fallback)
        {
            var text = read(name);
            if (text is null) return fallback;
            var parts = text.Split('+', StringSplitOptions.TrimEntries);
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            if (parts.Length < 2 || !Enum.TryParse<HotkeyKey>(parts[^1], true, out var key) ||
                !Enum.IsDefined(key) || key == HotkeyKey.F12 || !parts[^1].StartsWith('F'))
                throw new ArgumentException($"Invalid {name}: use named modifiers + F8..F11; F12 is reserved.");
            foreach (var part in parts[..^1])
            {
                var modifier = part.ToUpperInvariant() switch
                {
                    "ALT" => HotkeyModifiers.Alt,
                    "CONTROL" or "CTRL" => HotkeyModifiers.Control,
                    "SHIFT" => HotkeyModifiers.Shift,
                    "WIN" => HotkeyModifiers.Win,
                    _ => throw new ArgumentException($"Invalid modifier in {name}.")
                };
                if ((modifiers & modifier) != 0) throw new ArgumentException($"Repeated modifier in {name}.");
                modifiers |= modifier;
            }
            return new(modifiers, key);
        }
    }

    public static OverlayHotkeys Default { get; } = new(
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyKey.F8),
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyKey.F9),
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyKey.F10));
}

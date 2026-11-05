# Window, table and notification area

The interface remains English. Standard Windows title-bar dragging, resize borders and minimize/maximize/close controls are retained. Click-through is optional and the current mode/recovery shortcut is visible beside the title. Table values are display-only formatting; sorting and calculations still use decimal source values, with Unknown sorted last. Values smaller than 0.0001 use scientific notation instead of rounding to zero.

On Windows, App owns one Avalonia TrayIcon until shutdown. Tooltip: `POE Flip Overlay`. Left click toggles visibility; the menu offers Show/Hide overlay, Settings (expands the existing panel), and Exit. Closing the main window explains the notification-area behavior once per session and hides it. Explicit Exit and OS/application shutdown bypass close-to-tray. Hotkeys and background work are released on actual shutdown, not when the window is merely hidden.

Windows single-instance coordination uses a per-user, per-session named mutex and activation event, before opening storage or starting the UI. A second launch signals the primary to show an interactive window. A tray/menu/second-launch activation may focus the window because it is an explicit user action; background refresh does not activate it.

Avalonia 12.1.5's Win32 TrayIconImpl handles `TaskbarCreated` and re-adds icons after Explorer restarts. Source inspected: https://github.com/AvaloniaUI/Avalonia/blob/12.1.5/src/Windows/Avalonia.Win32/TrayIconImpl.cs . This is source-level support, not a claim that Explorer was restarted during verification.

Icon source: `design/app-icon-source.png`, approved by the user. Regenerate Windows icon resources with `tools/create-icon.ps1` (Windows System.Drawing). ICO contains PNG-backed 16/24/32/48/64/128/256 px frames. The same icon is embedded into the EXE, Avalonia resources, the native window/tray and the Inno Setup bootstrapper.

Currency sprites used for their exact in-game identity are Grinding Gear Games artwork, retrieved from the official CDN:
- Chaos Orb: https://web.poecdn.com/image/Art/2DItems/Currency/CurrencyRerollRare.png
- Divine Orb: https://web.poecdn.com/image/Art/2DItems/Currency/CurrencyModValues.png

Visual references inspected: https://github.com/SnosMe/awakened-poe-trade (official README showcase https://i.imgur.com/UTV6prE.png) and https://www.poeoverlay.com/ (market screenshot). They informed density and hierarchy; their UI code/styles were not copied.

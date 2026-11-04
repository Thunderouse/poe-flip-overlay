using FlipOverlay.Platform.Abstractions;
using Xunit;
namespace FlipOverlay.Platform.Tests;

public class CompanionTests
{
    [Fact]
    public void Companion_reports_unsupported_instead_of_claiming_overlay()
    {
        using var service = new UnsupportedCompanionService();
        Assert.Equal(PlatformCapabilities.None, service.Capabilities);
        Assert.Throws<PlatformNotSupportedException>(() => service.SetMode(OverlayMode.Passive));
        Assert.Throws<PlatformNotSupportedException>(() => service.Register(new HotkeyGesture(HotkeyModifiers.Control, HotkeyKey.F8), () => { }));
    }
}

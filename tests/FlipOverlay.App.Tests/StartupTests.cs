using Xunit;
namespace FlipOverlay.App.Tests;

public class StartupTests
{
    [Fact]
    public void Display_version_preserves_prerelease_and_omits_build_metadata()
    {
        var expected = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
        Assert.Equal(expected, Program.DisplayVersion);
    }
    [Fact]
    public void Offline_flags_never_construct_backend()
    {
        Program.BackendFactory = () => throw new InvalidOperationException("Backend must not be initialized");
        try { Assert.Equal(0, Program.Main(["--version"])); Assert.Equal(0, Program.Main(["--selftest"])); }
        finally { Program.BackendFactory = Program.CreateDefaultBackend; }
    }
}

using Avalonia;
using FlipOverlay.Infrastructure;
namespace FlipOverlay.App;

public static class Program
{
    public static string DisplayVersion => ((System.Reflection.AssemblyInformationalVersionAttribute?)
        Attribute.GetCustomAttribute(typeof(Program).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute)))
        ?.InformationalVersion.Split('+')[0] ?? typeof(Program).Assembly.GetName().Version!.ToString(3);
    public static Func<IWorkspaceBackend> BackendFactory { get; set; } = CreateDefaultBackend;
    public static IWorkspaceBackend CreateDefaultBackend()
    {
        var paths = AppDataPaths.Discover(AppContext.BaseDirectory);
        var repository = new LocalRepository(paths.DataDirectory);
        try
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            return new LocalWorkspaceBackend(repository, http, TimeProvider.System);
        }
        catch { repository.Dispose(); throw; }
    }
    public static bool UiSmoke { get; private set; }
    public static bool SmokeOpened { get; internal set; }
    internal static SingleInstance? Instance { get; private set; }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine(DisplayVersion); return 0; }
        if (args.Contains("--selftest")) return OfflineSelfTest.Run();
        UiSmoke = args.Contains("--ui-smoke"); SmokeOpened = false;
        if (UiSmoke) BackendFactory = () => new UnavailableWorkspaceBackend();
        using var instance = OperatingSystem.IsWindows() && !UiSmoke ? new SingleInstance() : null;
        if (instance is { IsPrimary: false }) return 0;
        Instance = instance;
        try
        {
            var result = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return UiSmoke ? (SmokeOpened ? 0 : 1) : result;
        }
        finally { Instance = null; }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
public static class OfflineSelfTest
{
    public static int Run()
    {
        try
        {
            var projection = new ResultProjection();
            projection.Replace([new("unknown", "Offline fixture"), new("two", "Offline fixture", Profit: 2), new("ten", "Offline fixture", Profit: 10)]);
            Require(projection.Rows.Select(x => x.Id).SequenceEqual(["ten", "two", "unknown"]), "numeric descending / unknown last");
            projection.SetSort(SortMetric.Profit, false);
            Require(projection.Rows.Select(x => x.Id).SequenceEqual(["two", "ten", "unknown"]), "numeric ascending / unknown last");
            projection.Selected = projection.Rows[0]; var selected = projection.Selected;
            projection.Frozen = true; projection.Replace([new("two", "Updated offline fixture", Profit: 20)]);
            Require(projection.HasPending && projection.Rows.Count == 3, "freeze defers updates");
            projection.Frozen = false;
            Require(projection.Rows.Count == 1 && ReferenceEquals(selected, projection.Selected), "incremental selection identity");
            var draft = new ManualEntryDraft { LeagueId = "offline", GiveCurrency = "A", ReceiveCurrency = "B", GiveQuantity = "2", ReceiveQuantity = "1", TimestampUnknown = true };
            Require(draft.ToEntry().ObservedAt is null && draft.ToEntry().Gold is null, "unknown time and gold remain unknown");
            Console.WriteLine("Offline selftest PASS: numeric sort both directions, unknown last, freeze, identity, unknown manual timestamp/gold. No GUI, storage or network initialized.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("Offline selftest FAIL: " + e.Message); return 1; }
    }
    private static void Require(bool condition, string assertion) { if (!condition) throw new InvalidOperationException(assertion); }
}

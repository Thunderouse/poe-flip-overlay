using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlipOverlay.App;
using FlipOverlay.Core;

// Offline, synthetic fixtures only. This executable never configures the production backend,
// accesses the network, or represents these rates/recipes as actual game data.
var output = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
var fixtureWatch = Stopwatch.StartNew();
var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
var context = new MarketContext("poe1", "pc", "OFFLINE-DIAGNOSTICS", "fixture-patch", "fixture-v1", "NPC Currency Exchange");
var valuations = Enumerable.Range(0, 10000).Select(i => new AggregateValuation(context,
    $"fixture-item-{i:D5}", 1 + i % 97 / 10m, "offline-diagnostics", null, now)).ToArray();
var recipes = Enumerable.Range(0, 5000).Select(i => new RecipeEdge($"fixture-edge-{i:D4}",
    valuations[i * 2].ItemId, valuations[i * 2 + 1].ItemId, new BatchRate(1, 2),
    context.Patch, "Offline synthetic diagnostic fixture; not a verified game recipe", now,
    "Fixture NPC", "Offline diagnostics only")).ToArray();
var manualRoutes = Enumerable.Range(0, 5000).Select(i => new ManualObservation[]
{
    new($"fixture-buy-{i}", context, "fixture-settlement", $"fixture-intermediate-{i}",
        [new($"stock-buy-{i}", new BatchRate(1, 2), 10000)], now, now, "Offline diagnostic fixture", true),
    new($"fixture-sell-{i}", context, $"fixture-intermediate-{i}", "fixture-settlement",
        [new($"stock-sell-{i}", new BatchRate(2, 2 + i % 3), 10000)], now, now, "Offline diagnostic fixture", true)
}).ToArray();
fixtureWatch.Stop();
var engine = new CandidateEngine();
var routeEngine = new RouteEngine();
var candidateRequest = new CandidateRequest(context, "fixture-settlement", 1000);
var routeRequest = new RouteRequest(context, "fixture-settlement", 1000, TimeSpan.FromMinutes(2));
IReadOnlyList<EstimatedCandidate> candidates = [];
RouteResult?[] evaluatedRoutes = [];
var projection = new ResultProjection();
var firstCandidate = Measure(() => candidates = engine.Evaluate(candidateRequest, valuations, recipes));
var firstRoutes = Measure(() => evaluatedRoutes = manualRoutes.Select(r => routeEngine.Evaluate(routeRequest, r, now)).ToArray());
var firstProjection = Measure(() => projection.Replace(Map(candidates)));
RequireCounts();
var samples = new List<object>();
var candidateMs = new List<double>();
var routeMs = new List<double>();
var projectionMs = new List<double>();
var sortMs = new List<double>();
var combinedMs = new List<double>();
var memoryBefore = Memory();
var gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
for (var i = 0; i < 30; i++)
{
    var cycle = Stopwatch.StartNew();
    var c = Measure(() => candidates = engine.Evaluate(candidateRequest, valuations, recipes));
    var r = Measure(() => evaluatedRoutes = manualRoutes.Select(route => routeEngine.Evaluate(routeRequest, route, now)).ToArray());
    var p = Measure(() => projection.Replace(Map(candidates)));
    // Alternating direction forces a real 5,000-row reordering rather than timing a no-op.
    var s = Measure(() => projection.SetSort(SortMetric.Profit, i % 2 != 0));
    cycle.Stop();
    RequireCounts();
    candidateMs.Add(c.ElapsedMs); routeMs.Add(r.ElapsedMs); projectionMs.Add(p.ElapsedMs);
    sortMs.Add(s.ElapsedMs); combinedMs.Add(cycle.Elapsed.TotalMilliseconds);
    samples.Add(new
    {
        Cycle = i + 1,
        CandidateCalculation = c,
        ManualRouteCalculation = r,
        ProjectionReplace = p,
        ProjectionSort = s,
        TotalMs = cycle.Elapsed.TotalMilliseconds
    });
}
var memoryAfter = Memory();
using var process = Process.GetCurrentProcess();
process.Refresh();
var cpuBefore = process.TotalProcessorTime;
var idleWatch = Stopwatch.StartNew();
Thread.Sleep(TimeSpan.FromSeconds(2));
process.Refresh();
idleWatch.Stop();
var idleCpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
var gcAfter = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
var commit = Command("git", "rev-parse", "--verify", "HEAD");
var status = Command("git", "status", "--porcelain");
var report = new
{
    SchemaVersion = 1,
    MeasuredAtUtc = DateTimeOffset.UtcNow,
    EvidenceKind = "macOS development-host console benchmark; not a Windows release gate",
    BuildConfiguration = BuildConfiguration(),
    Runtime = RuntimeInformation.FrameworkDescription,
    RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
    OS = RuntimeInformation.OSDescription,
    OSProductVersion = Command("sw_vers", "-productVersion").Text,
    OSBuild = Command("sw_vers", "-buildVersion").Text,
    CPU = Command("sysctl", "-n", "machdep.cpu.brand_string").Text,
    LogicalProcessors = Environment.ProcessorCount,
    Sdk = Command(Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")), "--version"),
    GitCommit = commit.ExitCode == 0 ? commit.Text : "UNBORN HEAD — no commit exists",
    Uncommitted = status.ExitCode == 0 && status.Text.Length > 0,
    GitStatus = status,
    Fixture = new
    {
        OfflineOnly = true,
        Valuations = valuations.Length,
        RecipeEdges = recipes.Length,
        ManualRoutes = manualRoutes.Length,
        LegsPerManualRoute = 2,
        Candidates = candidates.Count,
        AcceptedManualRoutes = evaluatedRoutes.Count(r => r is not null),
        ProjectionRows = projection.Rows.Count,
        Description = "Disjoint synthetic recipe pairs and independently confirmed two-leg synthetic routes; unknown gold; no source timestamp for aggregates"
    },
    FixtureConstructionMs = fixtureWatch.Elapsed.TotalMilliseconds,
    FirstInvocation = new
    {
        CandidateCalculation = firstCandidate,
        ManualRouteCalculation = firstRoutes,
        ProjectionReplace = firstProjection,
        Scope = "First engine/projection invocations after fixture preparation, includes relevant JIT; excludes process startup/build/restore"
    },
    Cycles = 30,
    PercentileMethod = "Nearest rank: sorted[ceil(0.95 * n) - 1]; 30 post-first-invocation samples; no forced GC",
    Summary = new
    {
        CandidateCalculation = Summary(candidateMs),
        ManualRouteCalculation = Summary(routeMs),
        ProjectionReplace = Summary(projectionMs),
        ProjectionSort = Summary(sortMs),
        CombinedCycle = Summary(combinedMs)
    },
    MemoryBeforeCycles = memoryBefore,
    MemoryAfterCycles = memoryAfter,
    GcCollectionCountBefore = gcBefore,
    GcCollectionCountAfter = gcAfter,
    Idle = new
    {
        WallMs = idleWatch.Elapsed.TotalMilliseconds,
        ProcessCpuMs = idleCpuMs,
        PercentOfOneLogicalCore = idleCpuMs / idleWatch.Elapsed.TotalMilliseconds * 100,
        Scope = "Two-second sleeping console process, no UI/rendering/provider activity; not overlay idle CPU"
    },
    Limitations = new[] { "No window, rendering, GPU, network, SQLite, game, or Windows platform adapter in this console process.",
        "Process WorkingSet64 and PrivateMemorySize64 are current macOS runtime counters, not Windows private working set or commit gates.",
        "Memory snapshots are not peaks or post-GC retained size; no GC.Collect, no forced full collection.",
        "Concurrent development-host load and tiered JIT/GC can affect 30-sample timing; this is not a controlled hardware comparison.",
        "Production projections with attached UI observers can cost more; headless virtualization tests are a separate check." },
    Samples = samples
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (output is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, json + Environment.NewLine);
}
Console.WriteLine(json);
GC.KeepAlive(valuations); GC.KeepAlive(recipes); GC.KeepAlive(manualRoutes);
GC.KeepAlive(candidates); GC.KeepAlive(evaluatedRoutes); GC.KeepAlive(projection);

void RequireCounts()
{
    if (candidates.Count != 5000 || evaluatedRoutes.Count(r => r is not null) != 5000 || projection.Rows.Count != 5000)
        throw new InvalidOperationException("Fixture did not exercise all expected routes/rows.");
}
static IEnumerable<ResultDto> Map(IReadOnlyList<EstimatedCandidate> rows) => rows.Select(c => new ResultDto(c.Id,
    $"OFFLINE {c.InputId} → {c.OutputId}", c.EstimatedProfit, c.EstimatedRoiPercent,
    Capital: c.CapitalRequired, Source: "Offline diagnostic fixture", Quality: c.Quality));
static Measurement Measure(Action action)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var watch = Stopwatch.StartNew();
    action();
    watch.Stop();
    return new(watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
}
static object Summary(List<double> values)
{
    var sorted = values.Order().ToArray();
    return new
    {
        MinimumMs = sorted[0],
        MedianMs = (sorted[14] + sorted[15]) / 2,
        P95Ms = sorted[(int)Math.Ceiling(.95 * sorted.Length) - 1],
        MaximumMs = sorted[^1],
        MeanMs = sorted.Average()
    };
}
static object Memory()
{
    using var p = Process.GetCurrentProcess(); p.Refresh();
    return new
    {
        WorkingSetBytes = p.WorkingSet64,
        PrivateBytes = p.PrivateMemorySize64,
        PrivateBytesInterpretation = p.PrivateMemorySize64 == 0 ? "Unavailable/zero runtime counter on this host; not evidence of zero private memory" : "Platform runtime counter; not Windows private working set",
        ManagedHeapBytesWithoutCollection = GC.GetTotalMemory(false)
    };
}
static CommandResult Command(string filename, params string[] args)
{
    try
    {
        var info = new ProcessStartInfo(filename) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
        return new(process.ExitCode, stdout.Trim(), stderr.Trim());
    }
    catch (Exception e) { return new(-1, "", e.Message); }
}
static string BuildConfiguration()
{
#if DEBUG
    return "Debug";
#else
    return "Release";
#endif
}
internal sealed record Measurement(double ElapsedMs, long AllocatedBytesOnCurrentThread);
internal sealed record CommandResult(int ExitCode, string Text, string Error);

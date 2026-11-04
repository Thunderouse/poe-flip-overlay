using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlipOverlay.Infrastructure;

public enum ReleaseCheckStatus { Disabled, UpToDate, UpdateAvailable, InvalidData, Offline, RateLimited, AccessDenied, Throttled, Superseded }
public sealed record ReleaseCheckResult(ReleaseCheckStatus Status, string? LatestVersion = null,
    Uri? ReleaseUrl = null, DateTimeOffset? CheckedAt = null, DateTimeOffset? NextCheckAt = null);

public sealed class ReleaseChecker(HttpScheduler scheduler, string? repository, string currentVersion, string userAgent)
{
    private readonly string? configuredRepository = ValidateRepository(repository);
    private static string? ValidateRepository(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > 140 || !Regex.IsMatch(value, @"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?/[A-Za-z0-9_.-]{1,100}\z", RegexOptions.CultureInvariant)
            || value.Split('/')[1] is "." or "..")
            throw new ArgumentException("Expected exact owner/repo", nameof(repository));
        return value;
    }
    private readonly object sync = new();
    private ReleaseCheckResult? last;
    private Task<ReleaseCheckResult>? flight;
    public Task<ReleaseCheckResult> CheckAsync(bool manual = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (flight is not null) return flight.WaitAsync(cancellationToken);
            if (!manual && last?.NextCheckAt > scheduler.UtcNow) return Task.FromResult(last);
            var completion = new TaskCompletionSource<ReleaseCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            flight = completion.Task;
            _ = RunAsync(manual, completion);
            return completion.Task.WaitAsync(cancellationToken);
        }
    }
    private async Task RunAsync(bool manual, TaskCompletionSource<ReleaseCheckResult> completion)
    {
        try
        {
            var result = await FetchAsync(manual);
            lock (sync)
            {
                if (result.Status != ReleaseCheckStatus.Disabled)
                {
                    var next = scheduler.UtcNow.AddHours(24);
                    if (result.NextCheckAt > next) next = result.NextCheckAt.Value;
                    result = result with { CheckedAt = scheduler.UtcNow, NextCheckAt = next };
                    last = result;
                }
                flight = null;
                completion.TrySetResult(result);
            }
        }
        catch (Exception ex)
        {
            lock (sync) { flight = null; completion.TrySetException(ex); }
        }
    }
    private async Task<ReleaseCheckResult> FetchAsync(bool manual)
    {
        if (configuredRepository is null) return new(ReleaseCheckStatus.Disabled);
        var current = ParseVersion(currentVersion);
        if (current is null) return new(ReleaseCheckStatus.InvalidData);
        var response = await scheduler.GetAsync("github-releases", new Uri($"https://api.github.com/repos/{configuredRepository}/releases/latest"), manual, userAgent: userAgent);
        if (response.Status is not (FetchStatus.Success or FetchStatus.Cached or FetchStatus.NotModified))
            return new(response.Status switch
            {
                FetchStatus.AccessDenied => ReleaseCheckStatus.AccessDenied,
                FetchStatus.RateLimited => ReleaseCheckStatus.RateLimited,
                FetchStatus.Throttled => ReleaseCheckStatus.Throttled,
                FetchStatus.Superseded => ReleaseCheckStatus.Superseded,
                FetchStatus.InvalidData => ReleaseCheckStatus.InvalidData,
                _ => ReleaseCheckStatus.Offline
            }, NextCheckAt: response.NextAllowedAt);
        if (response.Body is null) return new(ReleaseCheckStatus.InvalidData);
        try
        {
            using var json = JsonDocument.Parse(response.Body);
            var root = json.RootElement;
            var tag = ProviderJson.Text(root, "tag_name");
            var latest = ParseVersion(tag);
            if (latest is null || latest.Value.Prerelease || ProviderJson.Boolean(root, "draft") != false || ProviderJson.Boolean(root, "prerelease") != false)
                return new(ReleaseCheckStatus.InvalidData);
            var link = ProviderJson.Text(root, "html_url");
            var expected = $"https://github.com/{configuredRepository}/releases/tag/{tag}";
            var escaped = $"https://github.com/{configuredRepository}/releases/tag/{Uri.EscapeDataString(tag!)}";
            if (link != expected && link != escaped) return new(ReleaseCheckStatus.InvalidData);
            var comparison = 0;
            for (var i = 0; i < 3 && comparison == 0; i++) comparison = latest.Value.Numbers[i].CompareTo(current.Value.Numbers[i]);
            if (comparison == 0 && current.Value.Prerelease) comparison = 1;
            return new(comparison > 0 ? ReleaseCheckStatus.UpdateAvailable : ReleaseCheckStatus.UpToDate,
                tag, new Uri(link!), scheduler.UtcNow);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { return new(ReleaseCheckStatus.InvalidData); }
    }
    private static (BigInteger[] Numbers, bool Prerelease)? ParseVersion(string? value)
    {
        if (value is null || value.Length > 128) return null;
        var match = Regex.Match(value, @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var prerelease = match.Groups[4].Value;
        if (prerelease.Split('.').Any(x => x.Length > 1 && x[0] == '0' && x.All(char.IsAsciiDigit))) return null;
        return ([BigInteger.Parse(match.Groups[1].Value), BigInteger.Parse(match.Groups[2].Value), BigInteger.Parse(match.Groups[3].Value)], prerelease.Length != 0);
    }
}

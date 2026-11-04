using System.Net;
namespace FlipOverlay.Infrastructure;

public enum FetchStatus { Success, Cached, NotModified, Throttled, RateLimited, AccessDenied, NotFound, Offline, InvalidData, Superseded, Unsupported }
public sealed record HttpSnapshot(FetchStatus Status, byte[]? Body = null, DateTimeOffset? FetchedAt = null, DateTimeOffset? NextAllowedAt = null, string? ETag = null, bool IsStale = false);
public sealed class HttpScheduler(HttpClient client, TimeProvider time)
{
    public const int MaxPayloadBytes = 2 * 1024 * 1024;
    internal DateTimeOffset UtcNow => time.GetUtcNow();
    internal long Generation { get { lock (sync) return generation; } }
    private long generation;
    private CancellationTokenSource generationCancellation = new();
    public void AdvanceGeneration()
    {
        CancellationTokenSource old;
        lock (sync) { generation++; old = generationCancellation; generationCancellation = new(); flights.Clear(); cache.Clear(); }
        old.Cancel(); old.Dispose();
    }
    private readonly object sync = new();
    private readonly SemaphoreSlim network = new(2);
    private readonly Dictionary<(string, Uri), HttpSnapshot> cache = [];
    private readonly Dictionary<(string, Uri), Task<HttpSnapshot>> flights = [];
    private readonly HashSet<string> denied = [];
    private readonly Dictionary<string, DateTimeOffset> retry = [];
    private readonly Dictionary<string, int> failures = [];
    private readonly Dictionary<string, DateTimeOffset> manualGate = [];
    public Task<HttpSnapshot> GetAsync(string source, Uri uri, bool manual = false, CancellationToken cancellationToken = default, string? userAgent = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (source.Length > 128 || !uri.IsAbsoluteUri || uri.AbsoluteUri.Length > 4096) throw new ArgumentException("Invalid HTTP key");
        lock (sync)
        {
            var key = (source, uri);
            if (flights.TryGetValue(key, out var flight)) return flight.WaitAsync(cancellationToken);
            if (denied.Contains(source)) return Task.FromResult(new HttpSnapshot(FetchStatus.AccessDenied));
            if (retry.TryGetValue(source, out var until) && until > time.GetUtcNow()) return Task.FromResult(new HttpSnapshot(FetchStatus.RateLimited, NextAllowedAt: until));
            if (manual && manualGate.TryGetValue(source, out var gate) && gate > time.GetUtcNow()) return Task.FromResult(new HttpSnapshot(FetchStatus.Throttled, NextAllowedAt: gate));
            if (manual) manualGate[source] = time.GetUtcNow().AddSeconds(15);
            if (flights.Count >= 8) return Task.FromResult(new HttpSnapshot(FetchStatus.Throttled));
            cache.TryGetValue(key, out var old);
            if (old?.NextAllowedAt > time.GetUtcNow()) return Task.FromResult(old with { Status = FetchStatus.Cached });
            var completion = new TaskCompletionSource<HttpSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            flights[key] = completion.Task;
            _ = RunAsync(key, old, completion, generation, generationCancellation.Token, userAgent);
            return completion.Task.WaitAsync(cancellationToken);
        }
    }
    private async Task RunAsync((string Source, Uri Uri) key, HttpSnapshot? old, TaskCompletionSource<HttpSnapshot> completion, long version, CancellationToken token, string? userAgent)
    {
        try { completion.TrySetResult(await FetchAsync(key, old, version, token, userAgent)); }
        catch (Exception e) { completion.TrySetException(e); }
        finally { lock (sync) { if (flights.TryGetValue(key, out var current) && current == completion.Task) flights.Remove(key); } }
    }
    private HttpSnapshot Error((string Source, Uri Uri) key, HttpSnapshot? old, long version,
        FetchStatus status, DateTimeOffset? retryAfter = null)
    {
        lock (sync)
        {
            if (version != generation) return new(FetchStatus.Superseded);
            var count = Math.Min(failures.GetValueOrDefault(key.Source) + 1, 7);
            failures[key.Source] = count;
            var until = time.GetUtcNow().AddSeconds(Math.Min(300, 5 * Math.Pow(2, count - 1)));
            if (retryAfter > until) until = retryAfter.Value;
            retry[key.Source] = until;
            return old is null ? new(status, NextAllowedAt: until) :
                old with { Status = status, NextAllowedAt = until, IsStale = true };
        }
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaxPayloadBytes) throw new InvalidDataException("Payload limit exceeded");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + count > MaxPayloadBytes) throw new InvalidDataException("Payload limit exceeded");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }
    private async Task<HttpSnapshot> FetchAsync((string Source, Uri Uri) key, HttpSnapshot? old, long version, CancellationToken token, string? userAgent)
    {
        try { await network.WaitAsync(token); } catch (OperationCanceledException) { return new(FetchStatus.Superseded); }
        try
        {
            lock (sync)
            {
                if (version != generation) return new(FetchStatus.Superseded);
                if (denied.Contains(key.Source)) return new(FetchStatus.AccessDenied);
                if (retry.TryGetValue(key.Source, out var until) && until > time.GetUtcNow())
                    return new(FetchStatus.RateLimited, NextAllowedAt: until);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, key.Uri);
            if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (old?.ETag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", old.ETag);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            lock (sync)
            {
                if (version != generation) return new(FetchStatus.Superseded);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                { denied.Add(key.Source); return new(FetchStatus.AccessDenied); }
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var until = response.Headers.RetryAfter?.Date ?? time.GetUtcNow() + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
                return Error(key, old, version, FetchStatus.RateLimited, until);
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return new(FetchStatus.NotFound);
            if (response.StatusCode == HttpStatusCode.NotModified && old is null) return new(FetchStatus.InvalidData);
            if (response.StatusCode != HttpStatusCode.NotModified && !response.IsSuccessStatusCode)
                return Error(key, old, version, FetchStatus.Offline, response.Headers.RetryAfter?.Date ??
                    (response.Headers.RetryAfter?.Delta is { } delay ? time.GetUtcNow() + delay : null));
            var ttl = response.Headers.CacheControl?.NoCache == true ? TimeSpan.Zero :
                (response.Headers.CacheControl?.MaxAge ?? TimeSpan.Zero) - (response.Headers.Age ?? TimeSpan.Zero);
            var next = time.GetUtcNow() + (ttl > TimeSpan.Zero ? ttl : TimeSpan.Zero);
            var result = response.StatusCode == HttpStatusCode.NotModified && old is not null
                ? old with { Status = FetchStatus.NotModified, NextAllowedAt = next }
                : new HttpSnapshot(FetchStatus.Success, await ReadBoundedAsync(response.Content, token), time.GetUtcNow(), next, response.Headers.ETag?.ToString());
            lock (sync)
            {
                if (version != generation) return new(FetchStatus.Superseded);
                failures.Remove(key.Source);
                retry.Remove(key.Source);
                if (response.Headers.CacheControl?.NoStore == true) cache.Remove(key);
                else
                {
                    cache.Remove(key);
                    cache[key] = result;
                    while (cache.Count > 64 || cache.Values.Sum(x => (long)(x.Body?.Length ?? 0)) > 16 * 1024 * 1024)
                        cache.Remove(cache.Keys.First());
                }
            }
            return result;
        }
        catch (OperationCanceledException) { return token.IsCancellationRequested ? new(FetchStatus.Superseded) : Error(key, old, version, FetchStatus.Offline); }
        catch (InvalidDataException) { return new(FetchStatus.InvalidData); }
        catch (HttpRequestException) { return Error(key, old, version, FetchStatus.Offline); }
        catch (IOException) { return Error(key, old, version, FetchStatus.Offline); }
        finally { network.Release(); }
    }
}

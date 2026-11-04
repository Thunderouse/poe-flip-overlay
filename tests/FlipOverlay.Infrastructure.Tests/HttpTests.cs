using System.Net;
using System.Net.Http.Headers;
using FlipOverlay.Infrastructure;
using Xunit;

namespace FlipOverlay.Infrastructure.Tests;

public sealed class Clock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);
}
public sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Interlocked.Increment(ref Calls); return send(request, cancellationToken); }
    public static HttpResponseMessage Json(string json, int maxAge = 0, string? etag = null, int age = 0)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromSeconds(maxAge) };
        response.Headers.Age = TimeSpan.FromSeconds(age);
        if (etag is not null) response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }
}
public sealed class HttpTests
{
    [Fact]
    public async Task Manual_refresh_is_gated_and_access_denied_stops_source()
    {
        var clock = new Clock();
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var scheduler = new HttpScheduler(new HttpClient(handler), clock);
        Assert.Equal(FetchStatus.AccessDenied, (await scheduler.GetAsync("a", Endpoint, true)).Status);
        clock.Advance(30);
        Assert.Equal(FetchStatus.AccessDenied, (await scheduler.GetAsync("a", new Uri("https://example.test/other"), true)).Status);
        Assert.Equal(1, handler.Calls);
        var ok = new Handler((_, _) => Task.FromResult(Handler.Json("{}")));
        scheduler = new HttpScheduler(new HttpClient(ok), clock);
        await scheduler.GetAsync("b", Endpoint, true);
        Assert.Equal(FetchStatus.Throttled, (await scheduler.GetAsync("b", Endpoint, true)).Status);
        Assert.Equal(1, ok.Calls);
    }
    [Fact]
    public async Task Concurrent_callers_share_flight_and_network_limit_is_two()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, peak = 0;
        var handler = new Handler(async (_, _) => { var n = Interlocked.Increment(ref active); peak = Math.Max(peak, n); await release.Task; Interlocked.Decrement(ref active); return Handler.Json("{}"); });
        var scheduler = new HttpScheduler(new HttpClient(handler), new Clock());
        var tasks = Enumerable.Range(0, 6).Select(i => scheduler.GetAsync("a", new Uri($"https://example.test/{i / 2}"))).ToArray();
        await Task.Delay(50);
        var callsBeforeRelease = handler.Calls;
        release.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, callsBeforeRelease);
        Assert.Equal(3, handler.Calls);
        Assert.True(peak <= 2);
    }
    [Fact]
    public async Task Retry_after_blocks_source_and_offline_keeps_stale_original()
    {
        var clock = new Clock();
        var handler = new Handler((_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return Task.FromResult(r);
        });
        var scheduler = new HttpScheduler(new HttpClient(handler), clock);
        Assert.Equal(FetchStatus.RateLimited, (await scheduler.GetAsync("a", Endpoint)).Status);
        clock.Advance(89);
        Assert.Equal(FetchStatus.RateLimited, (await scheduler.GetAsync("a", new Uri("https://example.test/2"))).Status);
        Assert.Equal(1, handler.Calls);
        int count = 0;
        scheduler = new HttpScheduler(new HttpClient(new Handler((_, _) => ++count == 1 ? Task.FromResult(Handler.Json("{}")) : throw new HttpRequestException())), clock);
        var first = await scheduler.GetAsync("a", Endpoint);
        clock.Advance(5);
        var offline = await scheduler.GetAsync("a", Endpoint);
        Assert.Equal(FetchStatus.Offline, offline.Status);
        Assert.True(offline.IsStale);
        Assert.Equal(first.FetchedAt, offline.FetchedAt);
    }
    [Fact]
    public async Task Generation_cancels_and_discards_late_response()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        var handler = new Handler(async (_, token) => { captured = token; await release.Task; return Handler.Json("old", 3600); });
        var scheduler = new HttpScheduler(new HttpClient(handler), new Clock());
        var old = scheduler.GetAsync("a", Endpoint);
        scheduler.AdvanceGeneration();
        Assert.True(captured.IsCancellationRequested);
        release.SetResult();
        Assert.Equal(FetchStatus.Superseded, (await old).Status);
        Assert.Equal(FetchStatus.Success, (await scheduler.GetAsync("a", Endpoint)).Status);
        Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task Server_error_and_unexpected_not_modified_do_not_become_successful_prices()
    {
        var clock = new Clock();
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.InternalServerError, HttpStatusCode.NotModified]);
        var scheduler = new HttpScheduler(new HttpClient(new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage(responses.Dequeue()) { Content = new StringContent("{\"price\":999}") }))), clock);
        var first = await scheduler.GetAsync("source", Endpoint);
        Assert.Equal(FetchStatus.Offline, first.Status);
        Assert.Null(first.Body);
        clock.Advance(5);
        var second = await scheduler.GetAsync("source", Endpoint);
        Assert.Equal(FetchStatus.InvalidData, second.Status);
        Assert.Null(second.Body);
    }
    [Theory]
    [InlineData("no-store, max-age=3600")]
    [InlineData("no-cache, max-age=3600")]
    public async Task Cache_directives_require_network(string directive)
    {
        var handler = new Handler((_, _) =>
        {
            var response = Handler.Json("{}", 3600, "\"one\"");
            response.Headers.CacheControl = CacheControlHeaderValue.Parse(directive);
            return Task.FromResult(response);
        });
        var scheduler = new HttpScheduler(new HttpClient(handler), new Clock());
        await scheduler.GetAsync("a", Endpoint);
        await scheduler.GetAsync("a", Endpoint);
        Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task Oversized_payload_is_rejected_before_buffering()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(new byte[2 * 1024 * 1024 + 1]) }));
        var result = await new HttpScheduler(new HttpClient(handler), new Clock()).GetAsync("a", Endpoint);
        Assert.Equal(FetchStatus.InvalidData, result.Status);
        Assert.Null(result.Body);
    }
    [Fact]
    public async Task Cache_entry_and_byte_limits_evict_oldest()
    {
        var payload = new string('x', 1024 * 1024);
        var handler = new Handler((_, _) => Task.FromResult(Handler.Json(payload, 3600)));
        var scheduler = new HttpScheduler(new HttpClient(handler), new Clock());
        for (var i = 0; i < 18; i++) await scheduler.GetAsync("a", new Uri($"https://example.test/{i}"));
        Assert.Equal(FetchStatus.Success, (await scheduler.GetAsync("a", new Uri("https://example.test/0"))).Status);
        Assert.Equal(19, handler.Calls);
        handler = new Handler((_, _) => Task.FromResult(Handler.Json("{}", 3600)));
        scheduler = new HttpScheduler(new HttpClient(handler), new Clock());
        for (var i = 0; i < 66; i++) await scheduler.GetAsync("a", new Uri($"https://example.test/{i}"));
        Assert.Equal(FetchStatus.Success, (await scheduler.GetAsync("a", new Uri("https://example.test/0"))).Status);
        Assert.Equal(67, handler.Calls);
    }
    [Fact]
    public async Task Errors_back_off_exponentially_and_retry_after_applies_to_503()
    {
        var clock = new Clock();
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var scheduler = new HttpScheduler(new HttpClient(handler), clock);
        var first = await scheduler.GetAsync("a", Endpoint);
        Assert.Equal(clock.Now.AddSeconds(5), first.NextAllowedAt);
        await scheduler.GetAsync("a", Endpoint);
        Assert.Equal(1, handler.Calls);
        clock.Advance(5);
        Assert.Equal(clock.Now.AddSeconds(10), (await scheduler.GetAsync("a", Endpoint)).NextAllowedAt);
        clock.Advance(10);
        handler = new Handler((_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            r.Headers.RetryAfter = new RetryConditionHeaderValue(clock.Now.AddSeconds(123));
            return Task.FromResult(r);
        });
        scheduler = new HttpScheduler(new HttpClient(handler), clock);
        Assert.Equal(clock.Now.AddSeconds(123), (await scheduler.GetAsync("a", Endpoint)).NextAllowedAt);
    }
    [Fact]
    public async Task Queued_requests_recheck_source_denial()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, _) => { await release.Task; return new HttpResponseMessage(HttpStatusCode.Forbidden); });
        var scheduler = new HttpScheduler(new HttpClient(handler), new Clock());
        var tasks = Enumerable.Range(0, 4).Select(i => scheduler.GetAsync("a", new Uri($"https://example.test/{i}"))).ToArray();
        Assert.Equal(2, handler.Calls);
        release.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.All(results, x => Assert.Equal(FetchStatus.AccessDenied, x.Status));
        Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task Pending_queue_is_bounded_and_cancelled_waiter_does_not_cancel_shared_flight()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, _) => { await release.Task; return Handler.Json("{}"); });
        var scheduler = new HttpScheduler(new HttpClient(handler), new Clock());
        using var cancel = new CancellationTokenSource();
        var waiter = scheduler.GetAsync("a", Endpoint, cancellationToken: cancel.Token);
        var shared = scheduler.GetAsync("a", Endpoint);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        var others = Enumerable.Range(0, 7).Select(i => scheduler.GetAsync("a", new Uri($"https://example.test/{i}"))).ToArray();
        var overflow = scheduler.GetAsync("a", new Uri("https://example.test/overflow"));
        Assert.True(overflow.IsCompleted);
        Assert.Equal(FetchStatus.Throttled, (await overflow).Status);
        release.SetResult();
        Assert.Equal(FetchStatus.Success, (await shared.WaitAsync(TimeSpan.FromSeconds(2))).Status);
        await Task.WhenAll(others).WaitAsync(TimeSpan.FromSeconds(2));
    }
    private static readonly Uri Endpoint = new("https://example.test/data");
    [Fact]
    public async Task Cache_respects_age_and_conditional_304_preserves_body_fetch_time()
    {
        var clock = new Clock();
        var handler = new Handler((request, _) =>
        {
            if (request.Headers.IfNoneMatch.Count == 0) return Task.FromResult(Handler.Json("{\"sourceTime\":1}", 60, "\"one\"", 50));
            Assert.Equal("\"one\"", request.Headers.IfNoneMatch.Single().Tag);
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromSeconds(60) };
            return Task.FromResult(response);
        });
        var scheduler = new HttpScheduler(new HttpClient(handler), clock);
        var first = await scheduler.GetAsync("a", Endpoint);
        clock.Advance(9);
        Assert.Equal(FetchStatus.Cached, (await scheduler.GetAsync("a", Endpoint)).Status);
        Assert.Equal(1, handler.Calls);
        clock.Advance(2);
        var validated = await scheduler.GetAsync("a", Endpoint);
        Assert.Equal(FetchStatus.NotModified, validated.Status);
        Assert.Equal(first.FetchedAt, validated.FetchedAt);
        Assert.Equal(first.Body, validated.Body);
        Assert.Equal(2, handler.Calls);
    }
}

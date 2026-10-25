// Waits until the page has had no page/XHR/fetch request in flight for a moment.
// WaitForLoadState(NetworkIdle) returns at once after the first load, so it cannot
// see the AJAX refreshes NWEA does after a dropdown or radio change.

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Playwright;

namespace PlaybookRunner;

public interface INetworkQuiet
{
    /// <summary>True once quiet for <paramref name="quietMs"/>, or false when <paramref name="timeoutMs"/> passes first.</summary>
    Task<bool> WaitQuietAsync(int timeoutMs, int quietMs = NetworkTracker.DefaultQuietMs);
}

public sealed class NetworkTracker : INetworkQuiet
{
    public const int DefaultQuietMs = 500;
    private const int PollMs = 100;
    private const long StaleMs = 30_000; // ignore requests that never finish (long-polling, downloads)
    private static readonly HashSet<string> TrackedTypes = ["document", "xhr", "fetch"];

    // Playwright raises page events on its own thread, so this map must be thread-safe.
    private readonly ConcurrentDictionary<IRequest, long> _inFlight = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public NetworkTracker(IPage page)
    {
        page.Request += (_, r) =>
        {
            if (TrackedTypes.Contains(r.ResourceType)) _inFlight[r] = _clock.ElapsedMilliseconds;
        };
        page.RequestFinished += (_, r) => _inFlight.TryRemove(r, out var _);
        page.RequestFailed += (_, r) => _inFlight.TryRemove(r, out var _);
    }

    private bool Busy()
    {
        var now = _clock.ElapsedMilliseconds;
        return _inFlight.Values.Any(started => now - started < StaleMs);
    }

    public async Task<bool> WaitQuietAsync(int timeoutMs, int quietMs = DefaultQuietMs)
    {
        var deadline = _clock.ElapsedMilliseconds + timeoutMs;
        long? quietSince = Busy() ? null : _clock.ElapsedMilliseconds;
        while (_clock.ElapsedMilliseconds < deadline)
        {
            if (Busy()) quietSince = null;
            else if (quietSince is null) quietSince = _clock.ElapsedMilliseconds;
            else if (_clock.ElapsedMilliseconds - quietSince >= quietMs) return true;
            await Task.Delay(PollMs);
        }
        return false;
    }
}

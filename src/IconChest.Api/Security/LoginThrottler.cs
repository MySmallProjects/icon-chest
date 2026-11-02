using System.Collections.Concurrent;
using System.Net;
using IconChest.Core.Common;

namespace IconChest.Api.Security;

public interface ILoginThrottler
{
    (bool IsThrottled, int RetryAfterSeconds) CheckThrottled(IPAddress? clientIp);
    void RecordSuccess(IPAddress? clientIp);
    (bool IsThrottled, int RetryAfterSeconds) RecordFailure(IPAddress? clientIp);
    void Reset();
}

public sealed class LoginThrottler : ILoginThrottler
{
    private sealed class AttemptTracker
    {
        public List<DateTime> FailedTimestamps { get; } = new();
        public DateTime? BlockedUntilUtc { get; set; }
        public int ConsecutiveBlocks { get; set; }
    }

    private readonly ConcurrentDictionary<string, AttemptTracker> _trackers = new();
    private readonly IClock _clock;

    public LoginThrottler(IClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
    }

    public (bool IsThrottled, int RetryAfterSeconds) CheckThrottled(IPAddress? clientIp)
    {
        string key = GetIpKey(clientIp);
        if (!_trackers.TryGetValue(key, out var tracker))
        {
            return (false, 0);
        }

        lock (tracker)
        {
            var now = _clock.UtcNow;
            if (tracker.BlockedUntilUtc.HasValue && now < tracker.BlockedUntilUtc.Value)
            {
                int remainingSeconds = (int)Math.Ceiling((tracker.BlockedUntilUtc.Value - now).TotalSeconds);
                return (true, Math.Max(1, remainingSeconds));
            }

            return (false, 0);
        }
    }

    public void RecordSuccess(IPAddress? clientIp)
    {
        string key = GetIpKey(clientIp);
        _trackers.TryRemove(key, out _);
    }

    public void Reset()
    {
        _trackers.Clear();
    }

    public (bool IsThrottled, int RetryAfterSeconds) RecordFailure(IPAddress? clientIp)
    {
        string key = GetIpKey(clientIp);
        var tracker = _trackers.GetOrAdd(key, _ => new AttemptTracker());

        lock (tracker)
        {
            var now = _clock.UtcNow;
            var windowStart = now.AddMinutes(-1);

            // Clean older attempts
            tracker.FailedTimestamps.RemoveAll(t => t < windowStart);
            tracker.FailedTimestamps.Add(now);

            if (tracker.FailedTimestamps.Count >= 5)
            {
                tracker.ConsecutiveBlocks++;
                // Growing delay: 60s, 120s, 300s...
                int delaySeconds = tracker.ConsecutiveBlocks switch
                {
                    1 => 60,
                    2 => 120,
                    _ => 300
                };

                tracker.BlockedUntilUtc = now.AddSeconds(delaySeconds);
                return (false, 0);
            }

            return (false, 0);
        }
    }

    private static string GetIpKey(IPAddress? ip)
    {
        if (ip == null) return "127.0.0.1";
        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using IconChest.Core.Common;

namespace IconChest.Api.Security;

public sealed record SessionInfo(
    string Token,
    string Mode, // "secret" | "password"
    DateTime ExpiresUtc);

public interface ISessionManager
{
    SessionInfo CreateSession(string mode);
    SessionInfo? ValidateSession(string? token);
    bool InvalidateSession(string? token);
}

public sealed class SessionManager : ISessionManager
{
    private static readonly TimeSpan SlidingExpiry = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly IClock _clock;

    public SessionManager(IClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
    }

    public SessionInfo CreateSession(string mode)
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        var session = new SessionInfo(token, mode, _clock.UtcNow.Add(SlidingExpiry));
        _sessions[token] = session;
        return session;
    }

    public SessionInfo? ValidateSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (!_sessions.TryGetValue(token, out var session))
        {
            return null;
        }

        var now = _clock.UtcNow;
        if (now > session.ExpiresUtc)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        // Sliding expiry
        var updated = session with { ExpiresUtc = now.Add(SlidingExpiry) };
        _sessions[token] = updated;
        return updated;
    }

    public bool InvalidateSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        return _sessions.TryRemove(token, out _);
    }
}


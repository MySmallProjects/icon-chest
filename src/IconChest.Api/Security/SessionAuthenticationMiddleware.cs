using System.Net;
using Microsoft.AspNetCore.Http;

namespace IconChest.Api.Security;

public static class HttpContextSecurityExtensions
{
    private const string SessionItemKey = "IconChest_Session";

    public static void SetSession(this HttpContext context, SessionInfo session)
    {
        context.Items[SessionItemKey] = session;
    }

    public static SessionInfo? GetSession(this HttpContext context)
    {
        return context.Items.TryGetValue(SessionItemKey, out var s) ? s as SessionInfo : null;
    }

    public static bool IsLoopback(this HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null) return true; // In-memory TestServer / in-process caller
        return IPAddress.IsLoopback(remoteIp);
    }
}

public sealed class SessionAuthenticationMiddleware
{
    public const string SessionCookieName = "iconchest_session";

    private readonly RequestDelegate _next;
    private readonly ISessionManager _sessionManager;

    public SessionAuthenticationMiddleware(
        RequestDelegate next,
        ISessionManager sessionManager)
    {
        _next = next;
        _sessionManager = sessionManager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;

        // Check for session cookie
        if (context.Request.Cookies.TryGetValue(SessionCookieName, out var token))
        {
            var session = _sessionManager.ValidateSession(token);
            if (session != null)
            {
                context.SetSession(session);
            }
        }

        // Public endpoints that do NOT require authentication
        if (IsPublicEndpoint(path, method))
        {
            await _next(context);
            return;
        }

        // If path is under /api and not public, require valid session
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            if (context.GetSession() == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc7807",
                    title = "Unauthorized",
                    status = 401,
                    detail = "A valid session is required to access this endpoint.",
                    traceId = context.TraceIdentifier
                });
                return;
            }
        }

        await _next(context);
    }

    private static bool IsPublicEndpoint(string path, string method)
    {
        // 1. Health endpoint
        if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(method))
        {
            return true;
        }

        // 2. Session login and inspection
        if (string.Equals(path, "/api/session", StringComparison.OrdinalIgnoreCase) &&
            (HttpMethods.IsPost(method) || HttpMethods.IsGet(method)))
        {
            return true;
        }

        // 3. Swagger documentation and UI pages (calls from Swagger still require session)
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 4. Static files and SPA fallback (not under /api)
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

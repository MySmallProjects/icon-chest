using System.Text.Json.Serialization;
using IconChest.Api.Security;
using IconChest.Core.Security;
using IconChest.Core.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed record LoginRequest(
    [property: JsonPropertyName("secret")] string? Secret,
    [property: JsonPropertyName("password")] string? Password);

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app, string expectedSecret)
    {
        var group = app.MapGroup("/api/session").WithTags("Session");

        group.MapPost("/", (
            HttpContext context,
            LoginRequest request,
            ISessionManager sessionManager,
            ILoginThrottler throttler,
            ISettingsService settingsService) =>
        {
            var clientIp = context.Connection.RemoteIpAddress;

            // 1. Check throttling
            var (isThrottled, retryAfter) = throttler.CheckThrottled(clientIp);
            if (isThrottled)
            {
                context.Response.Headers.RetryAfter = retryAfter.ToString();
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    "Too Many Requests",
                    $"Too many failed login attempts. Please wait {retryAfter} seconds.");
            }

            bool authenticated = false;
            string mode = "secret";

            // 2. Secret authentication (loopback only - via secret or password field)
            var secretCandidate = !string.IsNullOrWhiteSpace(request.Secret)
                ? request.Secret
                : (context.IsLoopback() ? request.Password : null);

            if (!string.IsNullOrWhiteSpace(secretCandidate) && context.IsLoopback())
            {
                if (PasswordHasher.VerifySecret(secretCandidate, expectedSecret))
                {
                    authenticated = true;
                    mode = "secret";
                }
            }

            // 3. Password authentication (allowed from anywhere when LAN password is set)
            if (!authenticated && !string.IsNullOrWhiteSpace(request.Password))
            {
                var lanPasswordHash = settingsService.Current.Api.LanPasswordHash;
                if (!string.IsNullOrWhiteSpace(lanPasswordHash))
                {
                    if (PasswordHasher.VerifyPassword(request.Password, lanPasswordHash))
                    {
                        authenticated = true;
                        mode = "password";
                    }
                }
            }

            if (!authenticated)
            {
                var (nowThrottled, afterSecs) = throttler.RecordFailure(clientIp);
                if (nowThrottled)
                {
                    context.Response.Headers.RetryAfter = afterSecs.ToString();
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status429TooManyRequests,
                        "Too Many Requests",
                        $"Too many failed login attempts. Please wait {afterSecs} seconds.");
                }

                // Never reveal whether the secret or password was the problem
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Invalid secret or password.");
            }

            // Success
            throttler.RecordSuccess(clientIp);
            var session = sessionManager.CreateSession(mode);

            context.Response.Cookies.Append(
                SessionAuthenticationMiddleware.SessionCookieName,
                session.Token,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    Secure = false // Plain HTTP accepted limitation
                });

            return Results.Ok(new
            {
                authenticated = true,
                mode = session.Mode
            });
        })
        .WithName("Login")
        .WithSummary("Authenticates with session secret or LAN password");

        group.MapGet("/", (HttpContext context) =>
        {
            var session = context.GetSession();
            return Results.Ok(new
            {
                authenticated = session != null,
                mode = session?.Mode
            });
        })
        .WithName("GetSession")
        .WithSummary("Gets current session authentication status");

        group.MapDelete("/", (
            HttpContext context,
            ISessionManager sessionManager) =>
        {
            if (context.Request.Cookies.TryGetValue(SessionAuthenticationMiddleware.SessionCookieName, out var token))
            {
                sessionManager.InvalidateSession(token);
            }

            context.Response.Cookies.Delete(SessionAuthenticationMiddleware.SessionCookieName, new CookieOptions
            {
                Path = "/"
            });

            return Results.Ok(new { status = "logged_out" });
        })
        .WithName("Logout")
        .WithSummary("Logs out and clears current session cookie");
    }
}


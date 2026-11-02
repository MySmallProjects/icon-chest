using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using IconChest.Core.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IconChest.Api.Security;

public sealed class SecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SecurityMiddleware> _logger;

    public SecurityMiddleware(
        RequestDelegate next,
        ISettingsService settingsService,
        ILogger<SecurityMiddleware> logger)
    {
        _next = next;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();

        // 1. Host Header Allow-List Check (DNS rebinding protection)
        var host = context.Request.Host.Host;
        if (!IsHostAllowed(host))
        {
            _logger.LogWarning("Host header rejected: {Host}", host);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7807",
                title = "Bad Request",
                status = 400,
                detail = "Host header not permitted.",
                traceId = context.TraceIdentifier
            });
            return;
        }

        // 2. Origin Check on Non-GET Requests (No CORS, CSRF protection)
        var method = context.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
        {
            if (context.Request.Headers.TryGetValue("Origin", out var originValues) && originValues.Count > 0)
            {
                var origin = originValues.ToString();
                if (!string.IsNullOrWhiteSpace(origin) && !IsOriginAllowed(origin, context.Request.Host))
                {
                    _logger.LogWarning("Cross-origin request rejected from Origin: {Origin}", origin);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://tools.ietf.org/html/rfc7807",
                        title = "Forbidden",
                        status = 403,
                        detail = "Cross-origin requests are not permitted.",
                        traceId = context.TraceIdentifier
                    });
                    return;
                }
            }
        }

        // 3. Security Headers on Every Response
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Frame-Options"] = "DENY";

            var path = context.Request.Path.Value ?? string.Empty;
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
                {
                    // Relax CSP slightly for Swagger UI scripts/styles
                    headers["Content-Security-Policy"] =
                        "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; frame-ancestors 'none'";
                }
                else
                {
                    // Default strict CSP
                    headers["Content-Security-Policy"] =
                        "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; frame-ancestors 'none'";
                }
            }

            return Task.CompletedTask;
        });

        // 4. Continue pipeline
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            // Never log secrets, passwords, cookies, or login request bodies
            var route = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "/";
            _logger.LogDebug("{Method} {Route} responded {StatusCode} in {DurationMs} ms",
                method, route, context.Response.StatusCode, sw.ElapsedMilliseconds);
        }
    }

    private bool IsHostAllowed(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        // Loopback / localhost names are always allowed
        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("[::1]", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var settings = _settingsService.Current.Api;
        // When LAN mode is enabled and a password is configured, allow machine addresses and host name
        if (settings.AllowLan && !string.IsNullOrWhiteSpace(settings.LanPasswordHash))
        {
            try
            {
                var machineHostName = Dns.GetHostName();
                if (host.Equals(machineHostName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Check local network IP addresses
                var localIps = GetLocalIpAddresses();
                if (localIps.Contains(host, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Fall back
            }
        }

        return false;
    }

    private static bool IsOriginAllowed(string origin, HostString requestHost)
    {
        if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            bool hostMatches = string.Equals(originUri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase);
            bool portMatches = (originUri.Port == -1 && !requestHost.Port.HasValue) ||
                               (originUri.Port == (requestHost.Port ?? 80));

            return hostMatches && portMatches;
        }

        return false;
    }

    private static HashSet<string> GetLocalIpAddresses()
    {
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                {
                    ips.Add(ip.Address.ToString());
                }
            }
        }
        return ips;
    }
}

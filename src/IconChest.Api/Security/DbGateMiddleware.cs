using System.Text.Json;
using IconChest.Core.Data;
using Microsoft.AspNetCore.Http;

namespace IconChest.Api.Security;

public sealed class DbGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDbGate _dbGate;

    public DbGateMiddleware(RequestDelegate next, IDbGate dbGate)
    {
        _next = next;
        _dbGate = dbGate;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip non-db endpoints
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/events", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/data/restore", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/data/move", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/data/backup", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (_dbGate.IsMaintenanceActive)
        {
            await WriteMaintenanceResponseAsync(context);
            return;
        }

        IDisposable? lease = null;
        try
        {
            if (!_dbGate.TryEnterRead(out lease) || lease == null)
            {
                await WriteMaintenanceResponseAsync(context);
                return;
            }

            await _next(context);
        }
        catch (DbMaintenanceException)
        {
            if (!context.Response.HasStarted)
            {
                await WriteMaintenanceResponseAsync(context);
            }
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private static async Task WriteMaintenanceResponseAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "5";
        context.Response.ContentType = "application/problem+json";

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "https://iconchest.app/errors/maintenance",
            title = "Service Unavailable",
            status = 503,
            detail = "The database is currently undergoing maintenance. Please retry shortly."
        });

        await context.Response.Body.WriteAsync(payload);
    }
}


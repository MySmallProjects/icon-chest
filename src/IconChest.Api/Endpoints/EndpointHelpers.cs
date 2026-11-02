using Microsoft.AspNetCore.Http;

namespace IconChest.Api.Endpoints;

public static class EndpointHelpers
{
    public static IResult ProblemDetails(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        IDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = context.TraceIdentifier
        };

        if (errors != null && errors.Count > 0)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(
            detail: detail,
            statusCode: statusCode,
            title: title,
            type: $"https://tools.ietf.org/html/rfc7807#status-{statusCode}",
            extensions: extensions);
    }

    public static IResult ValidationError(HttpContext context, string field, string message)
    {
        return ProblemDetails(
            context,
            StatusCodes.Status400BadRequest,
            "Validation Error",
            message,
            new Dictionary<string, string[]>
            {
                [field] = new[] { message }
            });
    }

    public static IResult NotFound(HttpContext context, string message)
    {
        return ProblemDetails(context, StatusCodes.Status404NotFound, "Not Found", message);
    }

    public static IResult Conflict(HttpContext context, string message)
    {
        return ProblemDetails(context, StatusCodes.Status409Conflict, "Conflict", message);
    }
}


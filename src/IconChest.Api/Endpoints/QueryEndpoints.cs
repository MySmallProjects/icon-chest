using System.Text.Json.Serialization;
using IconChest.Core.Images;
using IconChest.Core.Query;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed class ValidateQueryRequest
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public sealed class ValidateQueryResponse
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<QueryError> Errors { get; set; } = Array.Empty<QueryError>();

    [JsonPropertyName("normalized")]
    public string? Normalized { get; set; }

    [JsonPropertyName("mentionsHidden")]
    public bool MentionsHidden { get; set; }

    [JsonPropertyName("mentionsMissing")]
    public bool MentionsMissing { get; set; }
}

public sealed class FromFilterResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("unsupportedReason")]
    public string? UnsupportedReason { get; set; }
}

public static class QueryEndpoints
{
    public static void MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/query").WithTags("Query");

        // 1. POST /api/query/validate
        group.MapPost("/validate", (ValidateQueryRequest? req) =>
        {
            var text = req?.Text ?? string.Empty;
            var res = QueryEngine.Validate(text);

            return Results.Ok(new ValidateQueryResponse
            {
                Valid = res.IsValid,
                Errors = res.Errors,
                Normalized = res.NormalizedText,
                MentionsHidden = res.MentionsHidden,
                MentionsMissing = res.MentionsMissing
            });
        })
        .WithName("ValidateQuery")
        .WithSummary("Validates a query string and returns syntax and semantic errors, normalized text, and mention flags");

        // 2. GET /api/query/help
        group.MapGet("/help", () =>
        {
            var help = QueryHelpGenerator.GetHelpModel();
            return Results.Ok(help);
        })
        .WithName("GetQueryHelp")
        .WithSummary("Returns reference documentation, grammar, fields, errors, limits, and examples for the query language");

        // 3. POST /api/query/from-filter
        group.MapPost("/from-filter", (ImageSearchRequest filter) =>
        {
            var result = SimpleFilterToQuery.Convert(filter);
            return Results.Ok(new FromFilterResponse
            {
                Success = result.Success,
                Text = result.QueryText,
                UnsupportedReason = result.UnsupportedReason
            });
        })
        .WithName("QueryFromFilter")
        .WithSummary("Converts a structured ImageSearchRequest filter into an equivalent query string");
    }
}


using System.Text.Json;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Exceptions;

namespace UrlShortener.Api.Middleware;

/// <summary>
/// Single place that turns exceptions into HTTP responses, so controllers
/// stay free of try/catch and every error path returns a consistent shape.
/// Domain exceptions map to specific, safe-to-expose status codes; anything
/// else is an unexpected fault and is logged with full detail but returned
/// to the client as a generic 500 (never leak internals/stack traces).
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, title) = MapException(ex);

            if (status >= 500)
                logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
            else
                logger.LogInformation("Request rejected ({Status}): {Message}", status, ex.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = status;

            var body = new ProblemDetailsResponse
            {
                Title = title,
                Status = status,
                Detail = status < 500 ? ex.Message : "An unexpected error occurred.",
                TraceId = context.TraceIdentifier,
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }
    }

    private static (int Status, string Title) MapException(Exception ex) => ex switch
    {
        ShortUrlNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
        ShortUrlExpiredException => (StatusCodes.Status410Gone, "Link Expired"),
        AliasAlreadyInUseException => (StatusCodes.Status409Conflict, "Alias Conflict"),
        InvalidLongUrlException => (StatusCodes.Status400BadRequest, "Invalid Request"),
        ShortCodeGenerationExhaustedException => (StatusCodes.Status503ServiceUnavailable, "Temporarily Unavailable"),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
    };
}

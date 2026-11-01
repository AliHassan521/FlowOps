using System.Net;
using System.Text.Json;
using FlowOps.Application.Common.Exceptions;

namespace FlowOps.Api.Middleware;

/// <summary>
/// Sits at the very start of the HTTP pipeline.
/// Catches ANY unhandled exception from any layer below it
/// and converts it into a clean JSON error response.
/// The client never sees a raw stack trace.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Pass the request down the pipeline normally.
            // If nothing goes wrong, _next completes and we return.
            await _next(context);
        }
        catch (Exception ex)
        {
            // Decide the HTTP status code based on exception type.
            var statusCode = ex switch
            {
                ConflictException         => HttpStatusCode.Conflict,
                Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => HttpStatusCode.Conflict,
                ForbiddenException        => HttpStatusCode.Forbidden,
                ArgumentNullException      => HttpStatusCode.BadRequest,
                ArgumentException          => HttpStatusCode.BadRequest,
                UnauthorizedAccessException => HttpStatusCode.Unauthorized,
                KeyNotFoundException      => HttpStatusCode.NotFound,
                _                         => HttpStatusCode.InternalServerError
            };

            if (statusCode >= HttpStatusCode.InternalServerError)
            {
                _logger.LogError(ex, "Unhandled exception occurred. TraceId: {TraceId}", context.TraceIdentifier);
            }
            else
            {
                _logger.LogInformation("Request failed with {StatusCode}. TraceId: {TraceId}", statusCode, context.TraceIdentifier);
            }

            // Build a clean error payload — this is what the client receives.
            var errorResponse = new
            {
                status  = (int)statusCode,
                error   = statusCode.ToString(),
                message = statusCode is HttpStatusCode.InternalServerError
                            ? "An unexpected error occurred. Please try again later."
                            : ex is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException
                                ? "The work item changed. Refresh and try again." : ex.Message,
                traceId = context.TraceIdentifier
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode  = (int)statusCode;

            var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}

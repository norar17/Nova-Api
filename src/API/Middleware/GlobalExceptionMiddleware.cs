using System.Net;
using System.Text.Json;
using ECommerce.Shared;
using ECommerce.Shared.Exceptions;

namespace ECommerce.API.Middleware;

/// <summary>
/// Catches every exception that escapes the pipeline and converts it into a consistent
/// <see cref="ApiResponse{T}"/> JSON body, logging full details server-side while returning
/// a safe, non-leaky message to the client.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, errorCode, message, errors) = exception switch
        {
            ValidationAppException validationEx => (
                validationEx.StatusCode, validationEx.ErrorCode, validationEx.Message,
                (IReadOnlyDictionary<string, string[]>?)validationEx.Errors),

            AppException appEx => (appEx.StatusCode, appEx.ErrorCode, appEx.Message, null),

            _ => ((int)HttpStatusCode.InternalServerError, "INTERNAL_ERROR",
                _environment.IsDevelopment() ? exception.Message : "An unexpected error occurred.", null)
        };

        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("Handled exception ({ErrorCode}) processing {Method} {Path}: {Message}",
                errorCode, context.Request.Method, context.Request.Path, message);
        }

        context.Response.StatusCode = statusCode;

        var response = ApiResponse<object>.Fail(message, errorCode, errors);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<GlobalExceptionMiddleware>();
}

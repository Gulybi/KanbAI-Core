using Microsoft.AspNetCore.Diagnostics;
using KanbAI_Core.DTOs;

namespace KanbAI_Core.Middleware;

/// <summary>
/// Catches all unhandled exceptions, logs diagnostics, and returns a standardised
/// <see cref="ApiResponse"/> so internal details are never leaked to API consumers.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception occurred while processing {RequestMethod} {RequestPath}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var message = _environment.IsDevelopment()
            ? $"{exception.GetType().Name}: {exception.Message}"
            : "An unexpected error occurred. Please try again later.";

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var response = ApiResponse.Fail(message);

        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }
}

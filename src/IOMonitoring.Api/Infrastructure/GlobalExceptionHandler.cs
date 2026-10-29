using FluentValidation;
using IOMonitoring.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace IOMonitoring.Api.Infrastructure;

public static class Policies
{
    public const string CanWrite = "CanWrite";
    public const string AdminOnly = "AdminOnly";
}

/*
 * Global exception handler for the API. Catches exceptions thrown by the application layer and converts them to HTTP responses.
 * Registered in Program.cs as a singleton.
 */
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException ve => new HttpValidationProblemDetails(
                ve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            NotFoundException nf => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource not found",
                Detail = nf.Message
            },
            ConflictException ce => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = ce.Message
            },
            _ => null
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception");
            return false; // let the default handler produce a 500
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, problem.GetType(), cancellationToken: cancellationToken);
        return true;
    }
}

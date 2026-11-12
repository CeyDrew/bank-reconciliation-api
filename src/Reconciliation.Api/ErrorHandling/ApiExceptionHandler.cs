using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Common;

namespace Reconciliation.Api.ErrorHandling;

/// <summary>Turns known exceptions into RFC 9457 problem responses with the right status code.</summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException validation => new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Resource not found", exception.Message),
            ConflictException => Problem(StatusCodes.Status409Conflict, "Conflict", exception.Message),
            DbUpdateConcurrencyException => Problem(StatusCodes.Status409Conflict, "Conflict",
                "The record was changed by someone else. Reload it and try again."),
            DomainException => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", exception.Message),
            _ => null
        };

        if (problem is null)
        {
            return false; // Unknown error: the default handler returns a generic 500 without details.
        }

        logger.LogInformation("Request failed with {Status}: {Message}", problem.Status, exception.Message);
        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}

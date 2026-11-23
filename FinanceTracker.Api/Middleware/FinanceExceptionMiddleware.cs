using FinanceTracker.Application.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinanceTracker.Api.Middleware;

public sealed class FinanceExceptionMiddleware(RequestDelegate next, ILogger<FinanceExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (FinanceValidationException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Request validation failed.", exception.Message, exception.Code);
        }
        catch (FinanceNotFoundException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, "Resource was not found.", exception.Message, "resource_not_found");
        }
        catch (FinanceConcurrencyException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status412PreconditionFailed, "The transaction changed.", exception.Message, "transaction_version_conflict");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, "A conflicting record already exists.", "Choose a different unique name.", "unique_constraint_conflict");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller disconnected; there is no response left to write.
        }
        catch (Exception exception)
        {
            var traceId = context.TraceIdentifier;
            logger.LogError(exception, "Unhandled API error for {Method} {Path}; trace {TraceId}", context.Request.Method, context.Request.Path, traceId);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "Use the traceId when reviewing local server logs.", "unexpected_error", traceId);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail, string code, string? traceId = null)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = context.Request.Path };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId ?? context.TraceIdentifier;
        await context.Response.WriteAsJsonAsync(problem);
    }
}

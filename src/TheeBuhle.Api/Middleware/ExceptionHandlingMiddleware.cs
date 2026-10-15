// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Api.Middleware;

/// <summary>
/// Turns a thrown exception into an HTTP status and a message a person can read.
/// </summary>
/// <remarks>
/// <para>
/// One place, so a controller never writes an error response and cannot get the
/// status wrong. The mapping is the interesting part, and each row of it is a
/// decision:
/// </para>
/// <list type="table">
/// <item>
/// <term><see cref="DomainRuleException"/> → 409 Conflict</term>
/// <description>
/// A business rule refused. Not 400: the request was well-formed, and the client
/// has nothing to fix by editing it. Not 422 either — the rule is about the
/// <em>state of the system</em> ("that learner already has a mentor"), not about
/// the shape of the payload.
/// </description>
/// </item>
/// <item>
/// <term><see cref="UnroutableConcernException"/> → 503 Service Unavailable</term>
/// <description>
/// A flagged safeguarding concern could not reach anybody, so the check-in was
/// <strong>not saved</strong>. 503 rather than 500 because it is genuinely
/// temporary and retryable, and because the message has to tell the mentor to
/// phone instead. The one exception whose response text matters more than its
/// status.
/// </description>
/// </item>
/// <item>
/// <term><see cref="AuthenticationException"/> → 401 Unauthorized</term>
/// <description>
/// With the service's own uniform message passed through unchanged, because that
/// message is deliberately identical for a wrong password, an unknown email and
/// a locked account.
/// </description>
/// </item>
/// <item>
/// <term>Everything else → 500</term>
/// <description>
/// With a generic message and a correlation id, and the real detail in the log.
/// </description>
/// </item>
/// </list>
/// <para>
/// <strong>An unexpected exception never reaches the client.</strong> A stack
/// trace names internal types, file paths and sometimes connection strings, and
/// a database error message frequently contains the SQL and the column names —
/// which is a free schema disclosure for anybody probing the API. The client
/// gets a correlation id; the log gets everything.
/// </para>
/// </remarks>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (UnroutableConcernException exception)
        {
            // Logged at Critical, not Error. This means the system currently
            // cannot receive a child-safety report, which is the loudest thing
            // it can have to say.
            ApiLog.ConcernUnroutable(logger, exception);

            await WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "safeguarding_unavailable",
                exception.Message);
        }
        catch (DomainRuleException exception)
        {
            // Debug, not Warning. A business rule refusing a request is the
            // system working — a parent trying to sign a form that closed
            // yesterday is not an incident, and logging it as one would bury
            // the real ones.
            ApiLog.RuleRefused(logger, exception.Message);

            await WriteAsync(context, StatusCodes.Status409Conflict, "rule_violation", exception.Message);
        }
        catch (AccessDeniedException exception)
        {
            // A decision that depends on the record, not just the caller's role:
            // programme staff may reset a parent's password but not an
            // administrator's. Same log level as a rule refusal, for the same
            // reason.
            ApiLog.RuleRefused(logger, exception.Message);

            await WriteAsync(context, StatusCodes.Status403Forbidden, "forbidden", exception.Message);
        }
        catch (UnauthenticatedException exception)
        {
            // Thrown by CurrentUserAccessor.Require() when an endpoint needs a
            // signed-in user and there is none — which means a controller is
            // missing an authorisation attribute. Mapped to 401 rather than
            // allowed to surface as a null-reference 500, so the bug is a wrong
            // status code rather than an outage.
            await WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "not_authenticated",
                exception.Message);
        }
        catch (AuthenticationException exception)
        {
            ApiLog.AuthenticationRefused(logger);

            await WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "authentication_failed",
                // The service's uniform message, passed through unchanged. It is
                // identical for a wrong password, an unknown email and a locked
                // account, and rewording it here would undo that.
                exception.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away. Not an error, and writing a response would
            // fail anyway — the socket is gone.
            ApiLog.RequestAborted(logger);
        }
        catch (Exception exception)
        {
            var correlationId = context.TraceIdentifier;

            ApiLog.UnhandledException(logger, correlationId, exception);

            await WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "server_error",
                "Something went wrong on our side. Nothing you did caused it. "
                + $"If you need to report it, quote reference {correlationId}.");
        }
    }

    private static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string message)
    {
        if (context.Response.HasStarted)
        {
            // Too late to change the status. Attempting it throws and replaces a
            // partial response with a worse one.
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = message,
            Instance = context.Request.Path,
        };

        problem.Extensions["correlationId"] = context.TraceIdentifier;

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, JsonOptions),
            context.RequestAborted);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static partial class ApiLog
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "A domain rule refused the request: {Reason}")]
    public static partial void RuleRefused(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Authentication was refused")]
    public static partial void AuthenticationRefused(ILogger logger);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Critical,
        Message = "A FLAGGED SAFEGUARDING CONCERN COULD NOT BE ROUTED. "
            + "The check-in was not saved and the mentor was told to phone. "
            + "Check that a designated safeguarding lead is configured.")]
    public static partial void ConcernUnroutable(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Debug,
        Message = "The client aborted the request")]
    public static partial void RequestAborted(ILogger logger);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Error,
        Message = "Unhandled exception, correlation {CorrelationId}")]
    public static partial void UnhandledException(ILogger logger, string correlationId, Exception exception);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Warning,
        Message = "Rate limit reached on {Path} from {ClientKey}")]
    public static partial void RateLimited(ILogger logger, string path, string clientKey);
}

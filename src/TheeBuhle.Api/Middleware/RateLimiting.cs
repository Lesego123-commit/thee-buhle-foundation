// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TheeBuhle.Api.Middleware;

/// <summary>
/// The rate-limit policies.
/// </summary>
/// <remarks>
/// <para>
/// A global ceiling plus two stricter policies, because the endpoints being
/// protected are being protected from different things:
/// </para>
/// <list type="bullet">
/// <item>
/// <strong>Everything</strong> gets the global limit — 120 requests with 60
/// replenished a minute. Generous enough that a real user cannot reach it, and
/// low enough to stop a runaway client. Applied globally rather than per
/// endpoint so a new controller arrives protected.
/// </item>
/// <item>
/// <strong>Sign-in</strong> is protected from password guessing. Five attempts
/// per five minutes, <em>on top of</em> the global limit.
/// </item>
/// <item>
/// <strong>Public forms</strong> are protected from spam and from somebody
/// enumerating whether an address is subscribed. Ten per hour.
/// </item>
/// </list>
/// <para>
/// <strong>The login limiter is independent of the account lock, and both are
/// needed.</strong> The account lock stops five wrong passwords against
/// <em>one</em> account, which is credential stuffing against a known user. The
/// rate limit stops one address trying five hundred <em>different</em> accounts,
/// which no per-account lock can see — each account gets one attempt and none of
/// them locks.
/// </para>
/// </remarks>
public static class RateLimiting
{
    public const string SignIn = "sign-in";

    public const string PublicForm = "public-form";

    public static void AddRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // ---------------------------------------------------------------
            //  The general limit, as a GLOBAL limiter rather than a policy an
            //  endpoint has to opt into.
            //
            //  Default-on is the point. A per-endpoint attribute means a new
            //  controller is unlimited until somebody remembers to decorate it,
            //  and the one nobody remembers is the one that matters. A global
            //  limiter also covers the health check and the static files, which
            //  no controller attribute would.
            //
            //  An endpoint that needs something stricter carries its own
            //  [EnableRateLimiting] and is then subject to BOTH — which is the
            //  behaviour wanted for sign-in: five per five minutes on top of the
            //  general ceiling, not instead of it.
            // ---------------------------------------------------------------
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => RateLimitPartition.GetTokenBucketLimiter(
                    KeyFor(context),
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 120,
                        TokensPerPeriod = 60,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

            // Password guessing. A fixed window rather than a sliding one on
            // purpose: the reset is predictable, so a locked-out legitimate user
            // can be told exactly how long to wait rather than "try again
            // later".
            options.AddPolicy(SignIn, context => RateLimitPartition.GetFixedWindowLimiter(
                KeyFor(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(5),

                    // No queue. Queueing a sixth login attempt holds a
                    // connection open to eventually reject it, which is a way of
                    // turning a rate limiter into a resource-exhaustion vector.
                    QueueLimit = 0,
                }));

            options.AddPolicy(PublicForm, context => RateLimitPartition.GetFixedWindowLimiter(
                KeyFor(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<ExceptionHandlingMiddleware>>();

                ApiLog.RateLimited(
                    logger,
                    context.HttpContext.Request.Path,
                    KeyFor(context.HttpContext));

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "application/problem+json";

                // A specific waiting time rather than "try again later". Somebody
                // who has genuinely mistyped their password five times deserves
                // to know whether to wait or to phone.
                var seconds = retryAfter == default ? 300 : (int)retryAfter.TotalSeconds;

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        status = StatusCodes.Status429TooManyRequests,
                        title = "too_many_requests",
                        detail = $"Too many attempts. Please wait about {Math.Max(1, seconds / 60)} "
                            + $"minute{(seconds >= 120 ? "s" : string.Empty)} and try again. "
                            + "If you are locked out, call us on +27 71 000 0000.",
                    },
                    cancellationToken);
            };
        });
    }

    /// <summary>
    /// The partition key: the signed-in user if there is one, otherwise the
    /// client address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-user where possible, because a shared connection is the normal case
    /// here rather than the exception — a Saturday session has thirty parents on
    /// one venue Wi-Fi network, and an address-keyed limiter would have them
    /// throttling each other.
    /// </para>
    /// <para>
    /// The forwarded-for header is read for the anonymous case because Azure App
    /// Service terminates TLS in front, so the direct connection address is
    /// always the platform's. Only the first entry is taken — a client can send
    /// any chain it likes, and a limiter that trusted the whole chain could be
    /// bypassed by prepending a random address per request.
    /// </para>
    /// </remarks>
    private static string KeyFor(HttpContext context)
    {
        var accessor = context.RequestServices.GetService<CurrentUserAccessor>();

        if (accessor?.User is { } user)
        {
            return $"user:{user.UserId}";
        }

        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        var address = string.IsNullOrWhiteSpace(forwarded)
            ? context.Connection.RemoteIpAddress?.ToString()
            : forwarded.Split(',')[0].Trim();

        return $"ip:{address ?? "unknown"}";
    }
}

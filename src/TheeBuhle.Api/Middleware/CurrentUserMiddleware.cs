// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.Security.Claims;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Api.Middleware;

/// <summary>
/// Rebuilds the request principal from the database, for every authenticated
/// request.
/// </summary>
/// <remarks>
/// <para>
/// JWT bearer authentication has already validated the token's signature and
/// expiry by the time this runs. What it has <em>not</em> done is check that the
/// account still exists, is still active, and still has the relationships the
/// request is about — because a JWT is a snapshot taken when it was signed.
/// </para>
/// <para>
/// So this middleware throws the token's claims away apart from the subject id,
/// and loads the real principal. That costs one indexed query per request and
/// buys three things:
/// </para>
/// <list type="bullet">
/// <item>
/// A <strong>suspension takes effect immediately</strong>, rather than whenever
/// the access token happens to expire. For a dismissed volunteer or a
/// compromised account, "within fifteen minutes" is not good enough.
/// </item>
/// <item>
/// A <strong>guardian link that was removed is gone immediately</strong>. If the
/// parent ids were in the token, a parent whose link to a child had been revoked
/// would keep reading that child's records until the token expired.
/// </item>
/// <item>
/// The JWT stays small and carries <strong>no personal information</strong>. A
/// signed token is readable by anybody holding it, and it sits in a browser's
/// storage — so it holds an id and a role and nothing else.
/// </item>
/// </list>
/// <para>
/// A token whose subject no longer resolves to an active account is treated as
/// unauthenticated rather than as an error, which is what makes a suspension
/// surface as a plain 401.
/// </para>
/// </remarks>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUserRepository users,
        CurrentUserAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(accessor);

        var subject = context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(subject, out var userId))
        {
            // Null when the account is suspended, invited-but-not-activated, or
            // deleted. The request then proceeds as anonymous and the
            // authorisation attributes refuse it.
            accessor.User = await users.FindPrincipalAsync(userId, context.RequestAborted);
        }

        await next(context);
    }
}

/// <summary>
/// Holds the principal for the lifetime of one request.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, so it is per-request by construction. A controller asks for this
/// rather than reaching into <c>HttpContext</c>, which keeps the services and
/// the controllers testable without a fake HTTP context.
/// </para>
/// <para>
/// <see cref="Require"/> throws rather than returning null, so a controller that
/// forgets an authorisation attribute fails loudly instead of operating on a
/// null user — and the exception is mapped to a 401 rather than surfacing as a
/// null-reference 500.
/// </para>
/// </remarks>
public sealed class CurrentUserAccessor
{
    public AuthenticatedUser? User { get; set; }

    public bool IsAuthenticated => User is not null;

    public AuthenticatedUser Require() =>
        User ?? throw new UnauthenticatedException();
}

/// <summary>Thrown when an endpoint needs a signed-in user and there is none.</summary>
public sealed class UnauthenticatedException()
    : Exception("You need to be signed in to do that.");

/// <summary>
/// The request's address and user agent, for the audit trail.
/// </summary>
/// <remarks>
/// <para>
/// The forwarded-for header is honoured, because Azure App Service terminates
/// TLS at its front end — so the direct connection is always from the platform
/// and the real client address only exists in that header.
/// </para>
/// <para>
/// Only the <em>first</em> entry is taken, and only when the platform is
/// actually in front. A client can send any forwarded-for value it likes, so
/// trusting the whole chain would let anybody write a chosen address into the
/// audit log — which is a log that cannot be corrected afterwards.
/// </para>
/// </remarks>
public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string IpAddress
    {
        get
        {
            var context = accessor.HttpContext;

            if (context is null)
            {
                // A background job, not a request. Recorded as "system" by the
                // other implementation; here it means the accessor was used
                // outside a request, which is worth being visible rather than
                // silently plausible.
                return "0.0.0.0";
            }

            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();

                // Azure appends a port to the forwarded address. Stripped so the
                // audit log holds an address rather than an address-and-port,
                // which would make two entries from one client look different.
                var colon = first.LastIndexOf(':');

                if (colon > 0 && !first.Contains("::", StringComparison.Ordinal))
                {
                    first = first[..colon];
                }

                return first;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
        }
    }

    public string UserAgent =>
        accessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault() ?? "unknown";
}

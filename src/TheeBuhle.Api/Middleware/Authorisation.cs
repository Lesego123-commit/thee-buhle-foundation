// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Api.Middleware;

/// <summary>
/// Requires a signed-in user, optionally in one of a set of roles.
/// </summary>
/// <remarks>
/// <para>
/// A filter rather than ASP.NET's own <c>[Authorize(Roles = ...)]</c>, because
/// the role that matters here is the one on the <em>rebuilt</em> principal, not
/// the one in the token's claims. A staff member promoted to safeguarding lead,
/// or a user suspended, is correct immediately — the token's role claim would be
/// up to fifteen minutes stale.
/// </para>
/// <para>
/// <strong>This is a coarse gate, not the authorisation.</strong> It answers
/// "may this kind of user reach this endpoint at all". Whether <em>this</em>
/// parent may read <em>that</em> learner is
/// <see cref="Application.Security.AccessPolicy"/>'s job, because it depends on
/// data. Confusing the two is how an API ends up with a role check that lets any
/// parent read any child.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireUserAttribute(params Role[] roles) : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var accessor = context.HttpContext.RequestServices.GetRequiredService<CurrentUserAccessor>();
        var user = accessor.User;

        if (user is null)
        {
            // 401, not 403. The distinction is worth keeping straight: 401 means
            // "we do not know who you are, sign in", and 403 means "we know who
            // you are and the answer is no". Sending 403 to an anonymous caller
            // makes a client show the wrong screen.
            context.Result = Problem(
                context,
                StatusCodes.Status401Unauthorized,
                "not_authenticated",
                "You need to be signed in to do that.");

            return Task.CompletedTask;
        }

        if (roles.Length > 0 && !roles.Contains(user.Role))
        {
            // The same sentence for every refusal, and it does not name the role
            // required. Telling a parent that an endpoint needs the
            // safeguarding-lead role maps out the system's privilege structure
            // one 403 at a time.
            context.Result = Problem(
                context,
                StatusCodes.Status403Forbidden,
                "not_permitted",
                "You do not have permission to do that.");
        }

        return Task.CompletedTask;
    }

    private static ObjectResult Problem(
        AuthorizationFilterContext context,
        int status,
        string code,
        string detail) =>
        new(new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail,
            Instance = context.HttpContext.Request.Path,
        })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
}

/// <summary>
/// Requires a staff-side account: staff, safeguarding lead or administrator.
/// </summary>
/// <remarks>
/// Spelled out as its own attribute rather than repeating the three roles at
/// twenty call sites, so adding a fourth staff-side role is one edit. The
/// grouping matches <c>RoleExtensions.IsStaffSide</c>, which is the one
/// definition of "staff-side" in the system.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireStaffAttribute()
    : RequireUserAttribute(Role.Staff, Role.SafeguardingLead, Role.Admin);

/// <summary>
/// Requires the designated safeguarding lead, or an administrator.
/// </summary>
/// <remarks>
/// <strong>Note who is absent: ordinary programme staff.</strong> That is the
/// point of the flag rather than an oversight — a flagged concern may be
/// <em>about</em> a member of programme staff, so the queue is not visible to
/// them. Task 1 section 9.3.13.7.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSafeguardingAttribute()
    : RequireUserAttribute(Role.SafeguardingLead, Role.Admin);

/// <summary>Requires an administrator.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAdminAttribute() : RequireUserAttribute(Role.Admin);

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// The dashboard screens: a parent's home, a learner's progress, a mentor's
/// mentees.
/// </summary>
/// <remarks>
/// <para>
/// Each endpoint returns a whole screen in one call. That is a deliberate
/// departure from one-resource-per-endpoint REST, and the reason is the network
/// the users actually have: the app opens on this screen, often on a slow and
/// expensive mobile connection in the Vaal. Seven round trips to render one
/// screen is seven chances to fail and seven TLS handshakes to pay for.
/// </para>
/// <para>
/// Bundling the reads does not bundle the permissions — <c>DashboardService</c>
/// still resolves every one through <c>AccessPolicy</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class DashboardController(
    DashboardService dashboard,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>The parent dashboard — screen 02 of the approved prototype.</summary>
    /// <param name="learnerId">
    /// Which child to show. Defaults to the primary guardian link, so a parent
    /// with one child never has to choose.
    /// </param>
    /// <response code="200">The screen.</response>
    /// <response code="409">
    /// No learners are linked to this account yet, or the requested learner is
    /// not one of theirs. Answered identically in both cases, so the endpoint
    /// cannot be used to discover which learner ids are real.
    /// </response>
    [HttpGet("dashboard")]
    [RequireUser(Role.Parent)]
    [ProducesResponseType(typeof(ParentDashboard), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ParentDashboard>> ParentDashboard(
        [FromQuery] Guid? learnerId,
        CancellationToken cancellationToken)
    {
        var screen = await dashboard.ForParentAsync(currentUser.Require(), learnerId, cancellationToken);

        return Ok(screen);
    }

    /// <summary>One learner's attendance and progress — screen 03.</summary>
    /// <remarks>
    /// <para>
    /// Reachable by a parent of that learner, the learner themselves, their
    /// active mentor, or staff — resolved on every request rather than from the
    /// token.
    /// </para>
    /// <para>
    /// The check-ins in the response are filtered <em>in the database query</em>
    /// by whether the caller may read flagged concerns. A parent's request never
    /// loads a safeguarding note, rather than loading one and removing it before
    /// serialising.
    /// </para>
    /// </remarks>
    [HttpGet("learners/{learnerId:guid}/progress")]
    [RequireUser]
    [ProducesResponseType(typeof(LearnerProgress), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LearnerProgress>> LearnerProgress(
        Guid learnerId,
        CancellationToken cancellationToken)
    {
        var screen = await dashboard.LearnerProgressAsync(
            currentUser.Require(),
            learnerId,
            cancellationToken);

        return Ok(screen);
    }

    /// <summary>A mentor's own home screen: their mentees, and what is overdue.</summary>
    /// <remarks>
    /// Takes no mentor id. The mentor is whoever is signed in, so there is no
    /// parameter a mentor could change to read another mentor's caseload — the
    /// endpoint has no way to express that request.
    /// </remarks>
    [HttpGet("mentor/dashboard")]
    [RequireUser(Role.Mentor)]
    [ProducesResponseType(typeof(MentorDashboard), StatusCodes.Status200OK)]
    public async Task<ActionResult<MentorDashboard>> MentorDashboard(CancellationToken cancellationToken)
    {
        var screen = await dashboard.ForMentorAsync(currentUser.Require(), cancellationToken);

        return Ok(screen);
    }
}

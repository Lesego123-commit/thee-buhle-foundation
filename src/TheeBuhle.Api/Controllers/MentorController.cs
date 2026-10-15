// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// The mentor's own endpoints: logging a weekly check-in.
/// </summary>
/// <remarks>
/// <para>
/// A check-in is the most sensitive write in the system, because it is where a
/// mentor records what a child told them. Two things about this controller are
/// worth reading:
/// </para>
/// <list type="bullet">
/// <item>
/// <strong>There is no endpoint to read a flagged check-in</strong>, not even
/// the mentor's own. Once flagged, a concern belongs to the safeguarding
/// process, and the author does not get to keep reading it. That is the rule
/// from Task 1 section 9.3.13.7, and it is enforced by the absence of a route
/// rather than by a check inside one.
/// </item>
/// <item>
/// <strong>A flagged check-in that cannot be routed is not saved.</strong> The
/// endpoint answers 503 with a phone number rather than 201. A mentor who
/// believes a child's situation has been reported when it reached nobody is the
/// worst outcome this system can produce.
/// </item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/mentor")]
[Produces("application/json")]
[RequireUser(Role.Mentor)]
public sealed class MentorController(
    MentorshipService mentorship,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Logs a weekly check-in.</summary>
    /// <remarks>
    /// <para>
    /// A <c>concerned</c> wellbeing rating flags the concern <strong>whether or
    /// not the box was ticked</strong>. A mentor should not be able to record
    /// that they are worried about a child and have it sit quietly in a note —
    /// so the rating alone is enough, and so is any of the urgent support areas
    /// even at a calmer rating.
    /// </para>
    /// <para>
    /// The response says whether it was escalated, so the app can tell the mentor
    /// what happened rather than leaving them to guess.
    /// </para>
    /// </remarks>
    /// <response code="201">Saved. <c>escalated</c> says whether it went to the safeguarding lead.</response>
    /// <response code="409">The assignment is not active, or the date is in the future.</response>
    /// <response code="503">
    /// The concern could not be routed to anybody, so the check-in was
    /// <strong>not saved</strong>. The message tells the mentor to phone.
    /// </response>
    [HttpPost("check-ins")]
    [ProducesResponseType(typeof(CheckInResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CheckInResponse>> LogCheckIn(
        [FromBody] LogCheckInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await mentorship.LogCheckInAsync(
            currentUser.Require(),
            request.AssignmentId,
            request.OccurredOn,
            request.Mode,
            request.Wellbeing,
            request.Notes,
            request.SupportAreas ?? [],
            request.ConcernFlagged,
            cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new CheckInResponse(
                result.CheckInId,
                result.Escalated,
                result.Escalated
                    ? "Check-in saved. Because a concern was flagged, it has gone straight to our "
                      + "safeguarding lead and is no longer visible to you — including in your own "
                      + "check-in history. If a child is in immediate danger, phone 10111 now."
                    : "Check-in saved."));
    }
}

public sealed record LogCheckInRequest
{
    [Required]
    public Guid AssignmentId { get; init; }

    [Required]
    public DateOnly OccurredOn { get; init; }

    [Required]
    public CheckInMode Mode { get; init; }

    [Required]
    public WellbeingRating Wellbeing { get; init; }

    /// <summary>
    /// What the mentor wrote.
    /// </summary>
    /// <remarks>
    /// Stored once, in the check-in, where the visibility rules apply to it. It
    /// is deliberately not copied into the audit trail or onto the domain event,
    /// because a note about a child should exist in exactly one place.
    /// </remarks>
    [MaxLength(4000)]
    public string? Notes { get; init; }

    /// <summary>
    /// Tags from a closed vocabulary. Anything unrecognised is dropped rather
    /// than stored, so the set the safeguarding rules match on stays closed.
    /// </summary>
    public IReadOnlyList<string>? SupportAreas { get; init; }

    /// <summary>
    /// Whether the mentor is explicitly raising this for staff attention.
    /// </summary>
    /// <remarks>
    /// False here does not mean "not a concern". A <c>Concerned</c> rating, or
    /// any of the urgent support areas, flags it regardless — this flag can only
    /// escalate, never suppress.
    /// </remarks>
    public bool ConcernFlagged { get; init; }
}

public sealed record CheckInResponse(Guid CheckInId, bool Escalated, string Message);

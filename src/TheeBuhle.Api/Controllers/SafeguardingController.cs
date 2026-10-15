// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Safeguarding;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// The safeguarding queue.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Restricted to the designated safeguarding lead and administrators.
/// Ordinary programme staff cannot reach any of it.</strong>
/// </para>
/// <para>
/// That is not over-caution. The whole point of flagging a concern is that it
/// may be <em>about</em> an adult in the child's life — possibly a member of
/// programme staff, possibly the mentor who wrote it. A queue visible to all
/// staff would make the flag meaningless in exactly the cases it exists for.
/// Task 1 section 9.3.13.7.
/// </para>
/// <para>
/// Every learner in this controller appears by reference code only. The lead can
/// look a child up; the queue itself does not put a list of named children on a
/// screen.
/// </para>
/// </remarks>
[ApiController]
[Route("api/safeguarding")]
[Produces("application/json")]
[RequireSafeguarding]
public sealed class SafeguardingController(
    IMentorshipRepository mentorships,
    IEscalationChain chain,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Open flagged concerns, oldest first.</summary>
    /// <remarks>
    /// Oldest first, which is the opposite of most lists in this system and
    /// deliberately so — a concern that has been waiting nine days is the one
    /// that needs attention, and a newest-first queue buries it.
    /// </remarks>
    [HttpGet("concerns")]
    [ProducesResponseType(typeof(IReadOnlyList<ConcernView>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ConcernView>>> Concerns(
        CancellationToken cancellationToken) =>
        Ok(await mentorships.OpenConcernsAsync(cancellationToken));

    /// <summary>Records that a concern has been dealt with, and how.</summary>
    /// <remarks>
    /// <para>
    /// The concern row is <strong>updated, never deleted</strong> — it is the
    /// safeguarding record and has to survive being resolved. Closing it takes it
    /// out of the queue and nothing else.
    /// </para>
    /// <para>
    /// The outcome is required and is free text. A dropdown would lose the part
    /// that matters if this record is ever needed: "spoke to the grandmother,
    /// referred to the clinic social worker, reviewing in two weeks" does not fit
    /// an enum.
    /// </para>
    /// </remarks>
    /// <response code="204">Closed.</response>
    /// <response code="409">Already closed, or not a flagged concern.</response>
    [HttpPost("concerns/{checkInId:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close(
        Guid checkInId,
        [FromBody] CloseConcernRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var closed = await mentorships.CloseConcernAsync(
            checkInId,
            currentUser.Require().UserId,
            request.Outcome.Trim(),
            cancellationToken);

        if (!closed)
        {
            // A double-submitted form, or two leads working the queue at once.
            // Reported rather than silently accepted, so the second person knows
            // their note was not the one recorded.
            return Conflict(new
            {
                detail = "That concern has already been closed, or it is not a flagged concern. "
                    + "Reload the queue to see the recorded outcome.",
            });
        }

        return NoContent();
    }

    /// <summary>The escalation chain, in order.</summary>
    /// <remarks>
    /// <para>
    /// Exposed as an endpoint rather than only living in documentation, because
    /// the organisation's child-protection policy is encoded in code and somebody
    /// has to be able to check that the code agrees with the policy without
    /// reading the code.
    /// </para>
    /// <para>
    /// It also answers the operational question this system is most likely to be
    /// asked: "if a mentor flags a concern right now, who receives it?"
    /// </para>
    /// </remarks>
    [HttpGet("escalation-chain")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> EscalationChain() => Ok(chain.Describe());
}

public sealed record CloseConcernRequest
{
    /// <summary>
    /// What was done about it, in the lead's own words.
    /// </summary>
    /// <remarks>
    /// Required, and a minimum length. A concern closed with an empty outcome is
    /// indistinguishable from one that was dismissed without being looked at, and
    /// this is the one record in the system where that distinction may eventually
    /// matter a great deal.
    /// </remarks>
    [Required]
    [MinLength(10)]
    [MaxLength(2000)]
    public required string Outcome { get; init; }
}

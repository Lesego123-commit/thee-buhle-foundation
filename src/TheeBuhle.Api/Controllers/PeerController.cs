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
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// Study buddies and study groups.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The most safeguarding-sensitive feature in the app, because it is the
/// one that connects one child to another.</strong> The rules — same grade only,
/// always pending, no second request after a refusal — live in
/// <see cref="PeerService"/> rather than here, so they hold for every caller
/// rather than only for these routes.
/// </para>
/// <para>Two properties of this controller are worth reading, though:</para>
/// <list type="bullet">
/// <item>
/// <strong>No endpoint takes a learner id for the caller.</strong> Every one
/// resolves the learner from the signed-in account, so "show me somebody else's
/// buddies" is not a request this API can express.
/// </item>
/// <item>
/// <strong>There is no messaging endpoint.</strong> Not here and not anywhere
/// else in this API. Buddies get each other's first name and a group meeting
/// time, and the conversation happens in a hall with a staff member in it.
/// </item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/peers")]
[Produces("application/json")]
[RequireUser(Role.Learner)]
public sealed class PeerController(
    PeerService peers,
    IBuddyPairingRepository pairing,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Learners in the same grade who might make good study buddies.</summary>
    /// <remarks>
    /// A first name, a reference code, a grade and the subjects they share.
    /// No surname, no suburb, no school, no contact details — enough to
    /// recognise somebody from Saturday, not enough to find a stranger's house.
    /// </remarks>
    [HttpGet("suggestions")]
    [ProducesResponseType(typeof(IReadOnlyList<BuddySuggestion>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<BuddySuggestion>>> Suggestions(
        CancellationToken cancellationToken) =>
        Ok(await peers.SuggestionsForAsync(currentUser.Require(), cancellationToken));

    /// <summary>The caller's own buddy requests, both directions.</summary>
    /// <remarks>
    /// The other learner appears as a first name and a surname initial —
    /// "Thabo M." — which is how the prototype's screens show a buddy.
    /// </remarks>
    [HttpGet("requests")]
    [ProducesResponseType(typeof(IReadOnlyList<BuddyRequestView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BuddyRequestView>>> Requests(
        CancellationToken cancellationToken)
    {
        var learnerId = currentUser.Require().LearnerId;

        return learnerId is null
            ? Ok(Array.Empty<BuddyRequestView>())
            : Ok(await pairing.RequestsForAsync(learnerId.Value, cancellationToken));
    }

    /// <summary>Asks to be paired with another learner.</summary>
    /// <remarks>
    /// Creates a <strong>pending</strong> request and nothing more. A mentor
    /// reviews every pairing before it exists, and the response says so.
    /// </remarks>
    /// <response code="202">Requested. A mentor will review it.</response>
    /// <response code="409">Not the same grade, yourself, or already asked.</response>
    [HttpPost("requests/{recipientId:guid}")]
    [ProducesResponseType(typeof(PairingRequestResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PairingRequestResult>> RequestPairing(
        Guid recipientId,
        CancellationToken cancellationToken)
    {
        var result = await peers.RequestPairingAsync(
            currentUser.Require(),
            recipientId,
            cancellationToken);

        // 202, not 201. Nothing usable has been created — a pending request is
        // an ask, and the status code should not imply the pairing exists.
        return Accepted(result);
    }

    /// <summary>Study groups for the caller's grade, with their own membership status.</summary>
    /// <remarks>
    /// Groups the learner is not in are included, because the point of the screen
    /// is to let them ask to join one. A null status means no membership row at
    /// all, which the app renders as "request to join" rather than as "pending".
    /// </remarks>
    [HttpGet("groups")]
    [ProducesResponseType(typeof(IReadOnlyList<StudyGroupCard>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<StudyGroupCard>>> Groups(
        CancellationToken cancellationToken) =>
        Ok(await peers.GroupsForAsync(currentUser.Require(), cancellationToken));

    /// <summary>Asks to join a study group.</summary>
    /// <remarks>
    /// Pending, for the same reason as a pairing: an adult signs off before a
    /// child is added to a group of other children.
    /// </remarks>
    [HttpPost("groups/{groupId:guid}/join")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> JoinGroup(Guid groupId, CancellationToken cancellationToken)
    {
        var message = await peers.RequestGroupMembershipAsync(
            currentUser.Require(),
            groupId,
            cancellationToken);

        return Accepted(new { status = "pending", message });
    }
}

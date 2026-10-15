// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Peer support: asking to be paired with another learner, and asking to join a
/// study group.
/// </summary>
/// <remarks>
/// <para>
/// This is the most safeguarding-sensitive feature in the system, because it is
/// the one that connects one child to another. The rules live here rather than
/// in the controller, and that placement is the point: a rule in a controller
/// holds only for the one endpoint that calls it, and the day the mobile app or
/// an admin-side form creates a pairing, the rule has to be remembered again.
/// </para>
/// <para>Three rules, all enforced here:</para>
/// <list type="number">
/// <item>
/// <strong>Same grade only.</strong> Pairing a Grade 4 learner with a Grade 11
/// learner is not a study arrangement.
/// </item>
/// <item>
/// <strong>Always pending.</strong> Nothing in this service can produce an
/// approved pairing — a staff member decides, through
/// <c>IBuddyPairingRepository.DecideRequestAsync</c>.
/// </item>
/// <item>
/// <strong>No second bite.</strong> A learner who has already been declined
/// cannot re-request, because a child being asked repeatedly by somebody they
/// have turned down is exactly what an adult should be preventing.
/// </item>
/// </list>
/// </remarks>
public sealed class PeerService(
    IPeerRepository peers,
    IBuddyPairingRepository pairing,
    ILearnerRepository learners,
    IUnitOfWork unitOfWork,
    IAuditRepository audit,
    IRequestContext requestContext,
    ILogger<PeerService> logger)
{
    /// <summary>How many buddy suggestions to offer.</summary>
    private const int SuggestionLimit = 12;

    public async Task<IReadOnlyList<BuddySuggestion>> SuggestionsForAsync(
        AuthenticatedUser learner,
        CancellationToken cancellationToken = default)
    {
        var me = await ResolveAsync(learner, cancellationToken);

        return await peers.SuggestedForAsync(me.Id, me.Grade, SuggestionLimit, cancellationToken);
    }

    public async Task<IReadOnlyList<StudyGroupCard>> GroupsForAsync(
        AuthenticatedUser learner,
        CancellationToken cancellationToken = default)
    {
        var me = await ResolveAsync(learner, cancellationToken);

        return await peers.GroupsForAsync(me.Id, me.Grade, cancellationToken);
    }

    /// <summary>
    /// Asks to be paired with another learner.
    /// </summary>
    /// <remarks>
    /// Creates a pending request and nothing else. The message returned says so
    /// explicitly — a learner told "sent" who then hears nothing for two days
    /// would reasonably think the app was broken, and telling them an adult
    /// reviews it is both true and reassuring.
    /// </remarks>
    public async Task<PairingRequestResult> RequestPairingAsync(
        AuthenticatedUser learner,
        Guid recipientId,
        CancellationToken cancellationToken = default)
    {
        var me = await ResolveAsync(learner, cancellationToken);

        if (me.Id == recipientId)
        {
            throw new DomainRuleException("You cannot pair with yourself.");
        }

        var recipient = await learners.FindAsync(recipientId, cancellationToken);

        if (recipient is null || recipient.Grade != me.Grade)
        {
            // One message for both cases. Distinguishing "no such learner" from
            // "different grade" would let somebody enumerate which learner ids
            // exist and what grade each child is in, one request at a time.
            throw new DomainRuleException(
                "You can only ask to study with somebody in your own grade.");
        }

        var existing = await pairing.RequestsForAsync(me.Id, cancellationToken);

        if (existing.Any(request => request.ReferenceCode == recipient.ReferenceCode))
        {
            // Covers a pending request, an approved one, and — deliberately — a
            // declined one. A child who has turned somebody down should not be
            // asked again through the app; if the two of them sort it out in
            // person, a mentor can approve it.
            throw new DomainRuleException(
                "You have already asked to study with that person, or they have already answered.");
        }

        return await unitOfWork.TransactionAsync(async () =>
        {
            var request = new BuddyRequest
            {
                RequesterId = me.Id,
                RecipientId = recipientId,
                Status = BuddyRequestStatus.Pending,
                UpdatedBy = learner.UserId,
            };

            pairing.CreateRequest(request);

            audit.Write(
                learner.UserId,
                "peer.pairing_requested",
                nameof(BuddyRequest),
                request.Id.ToString(),
                before: null,
                // Both children by pseudonym, never by name. This row is in a
                // table nobody ever deletes from.
                after: new
                {
                    Requester = me.ReferenceCode,
                    Recipient = recipient.ReferenceCode,
                    Grade = me.Grade,
                },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            Log.PairingRequested(logger, me.ReferenceCode, recipient.ReferenceCode);

            return new PairingRequestResult(
                request.Id,
                "Request sent. A mentor reviews every buddy request before a pairing is approved, "
                + "so you will hear back after the next Saturday session.");
        }, cancellationToken);
    }

    /// <summary>
    /// Asks to join a study group.
    /// </summary>
    /// <remarks>
    /// Pending, for the same reason as a pairing: an adult signs off before a
    /// child is added to a group of other children.
    /// </remarks>
    public async Task<string> RequestGroupMembershipAsync(
        AuthenticatedUser learner,
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        var me = await ResolveAsync(learner, cancellationToken);

        await unitOfWork.TransactionAsync(async () =>
        {
            await peers.RequestGroupMembershipAsync(groupId, me.Id, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return "Asked to join. A mentor will confirm at the next session.";
    }

    /// <summary>
    /// Resolves the caller's own learner record.
    /// </summary>
    /// <remarks>
    /// From the authenticated principal, never from a parameter. There is no
    /// method on this service that accepts "whose peers" as an argument, so a
    /// learner cannot ask it about a different learner — the request cannot be
    /// expressed.
    /// </remarks>
    private async Task<Learner> ResolveAsync(
        AuthenticatedUser learner,
        CancellationToken cancellationToken)
    {
        var learnerId = learner.LearnerId
            ?? throw new DomainRuleException("This account has no learner profile.");

        return await learners.FindAsync(learnerId, cancellationToken)
            ?? throw new DomainRuleException("This account has no learner profile.");
    }
}

public sealed record PairingRequestResult(Guid RequestId, string Message);

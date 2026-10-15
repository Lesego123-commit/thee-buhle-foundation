// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>Buddy suggestions and study groups.</summary>
public sealed class PeerRepository(TheeBuhleDbContext context, IClock clock) : IPeerRepository
{
    /// <summary>
    /// Learners in the same grade who might make good study buddies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The projection is the safeguarding decision.</strong> A first name,
    /// a reference code, a grade and the subjects they share — and nothing else.
    /// No surname, no suburb, no school, no contact details. Enough for a learner
    /// to recognise somebody from their Saturday group; not enough to find a
    /// stranger's house.
    /// </para>
    /// <para>
    /// Same grade only, and learners already connected or with a pending request
    /// are excluded — so the list does not invite a second request to somebody
    /// who has already declined, which is a small thing that matters when the
    /// people involved are children.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<BuddySuggestion>> SuggestedForAsync(
        Guid learnerId,
        int grade,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var mySubjects = await context.AssessmentResults
            .AsNoTracking()
            .Where(result => result.LearnerId == learnerId)
            .Select(result => result.SubjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return await context.Learners
            .AsNoTracking()
            .Where(learner => learner.Id != learnerId
                && learner.Grade == grade
                && learner.Enrolments.Any(enrolment => enrolment.Status == EnrolmentStatus.Active)
                // Anybody already in a request either direction, at any status,
                // is left out. A declined request must not reappear as a
                // suggestion the next day.
                && !context.BuddyRequests.Any(request =>
                    (request.RequesterId == learnerId && request.RecipientId == learner.Id)
                    || (request.RequesterId == learner.Id && request.RecipientId == learnerId)))
            .OrderBy(learner => learner.FirstName)
            .Take(limit)
            .Select(learner => new BuddySuggestion(
                learner.Id,
                learner.ReferenceCode,
                learner.FirstName,
                learner.Grade,
                learner.Results
                    .Where(result => mySubjects.Contains(result.SubjectId))
                    .Select(result => result.Subject.Name)
                    .Distinct()
                    .ToList()))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Study groups for one learner's grade, with their own membership status.
    /// </summary>
    /// <remarks>
    /// Groups the learner is not in are included, because the point of the screen
    /// is to let them ask to join one. Their status comes back as null when they
    /// have no membership row at all, which the app renders as a "request to
    /// join" button rather than as "pending".
    /// </remarks>
    public async Task<IReadOnlyList<StudyGroupCard>> GroupsForAsync(
        Guid learnerId,
        int grade,
        CancellationToken cancellationToken = default) =>
        await context.PeerGroups
            .AsNoTracking()
            .Where(group => group.Subject == null || group.Subject.Grade == grade)
            .OrderBy(group => group.Name)
            .Select(group => new StudyGroupCard(
                group.Id,
                group.Name,
                group.Subject != null ? group.Subject.Name : null,
                group.MeetsAt,
                group.Location,
                group.Memberships.Count(membership => membership.Status == MembershipStatus.Joined),
                group.Memberships
                    .Where(membership => membership.LearnerId == learnerId)
                    .Select(membership => (MembershipStatus?)membership.Status)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Asks to join a study group.
    /// </summary>
    /// <remarks>
    /// Always creates a <em>pending</em> membership, never an approved one — the
    /// same request-then-approve shape as buddy pairing, for the same reason. An
    /// adult signs off before a child is added to a group of other children, and
    /// there is deliberately no method on this class that creates an approved
    /// membership directly.
    /// </remarks>
    public async Task RequestGroupMembershipAsync(
        Guid groupId,
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.GroupMemberships
            .FirstOrDefaultAsync(
                membership => membership.GroupId == groupId && membership.LearnerId == learnerId,
                cancellationToken);

        if (existing is not null)
        {
            // Already asked, or already a member. Not an error, and deliberately
            // not a way to reset a declined membership back to pending.
            return;
        }

        await context.GroupMemberships.AddAsync(
            new GroupMembership
            {
                GroupId = groupId,
                LearnerId = learnerId,
                Status = MembershipStatus.Pending,
                JoinedAt = clock.Now,
            },
            cancellationToken);
    }

    public async Task SetGroupMembershipStatusAsync(
        Guid groupId,
        Guid learnerId,
        MembershipStatus status,
        CancellationToken cancellationToken = default) =>
        await context.GroupMemberships
            .Where(membership => membership.GroupId == groupId && membership.LearnerId == learnerId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(membership => membership.Status, status),
                cancellationToken);
}

/// <summary>
/// Buddy pairing requests.
/// </summary>
/// <remarks>
/// <para>
/// The most safeguarding-sensitive feature in the app, because it is the one
/// that connects one child to another.
/// </para>
/// <para>
/// Note what this class does <strong>not</strong> offer: no method creates an
/// approved pairing. The only way to reach
/// <see cref="BuddyRequestStatus.Approved"/> is
/// <see cref="DecideRequestAsync"/>, which requires a staff user id — so
/// request-then-approve is expressed in the available operations rather than in
/// a rule somebody has to remember. And nothing here returns a way to send a
/// message, because no learner-to-learner messaging endpoint exists anywhere in
/// the system.
/// </para>
/// </remarks>
public sealed class BuddyPairingRepository(TheeBuhleDbContext context, IClock clock) : IBuddyPairingRepository
{
    public Task<BuddyRequest?> FindRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default) =>
        context.BuddyRequests
            .Include(request => request.Requester)
            .Include(request => request.Recipient)
            .FirstOrDefaultAsync(request => request.Id == requestId, cancellationToken);

    public void CreateRequest(BuddyRequest request) => context.BuddyRequests.Add(request);

    /// <summary>
    /// Approves or declines a request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Conditional on the request still being pending, so two staff members
    /// opening the approval queue at once cannot have the second one silently
    /// overwrite the first one's decision — which in a two-person team is the
    /// normal case, not an edge case.
    /// </para>
    /// <para>
    /// The approver's id is recorded because a safeguarding decision has an
    /// author. "Who approved this pairing" has to be answerable.
    /// </para>
    /// </remarks>
    public async Task<bool> DecideRequestAsync(
        Guid requestId,
        BuddyRequestStatus decision,
        Guid approvedBy,
        CancellationToken cancellationToken = default)
    {
        var affected = await context.BuddyRequests
            .Where(request => request.Id == requestId && request.Status == BuddyRequestStatus.Pending)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(request => request.Status, decision)
                    .SetProperty(request => request.ApprovedBy, approvedBy)
                    .SetProperty(request => request.DecidedAt, clock.Now)
                    .SetProperty(request => request.UpdatedBy, approvedBy)
                    .SetProperty(request => request.UpdatedAt, clock.Now),
                cancellationToken);

        return affected > 0;
    }

    /// <summary>
    /// A learner's own requests, both directions.
    /// </summary>
    /// <remarks>
    /// The other learner appears as a first name and a surname
    /// <em>initial</em> — "Thabo M." — which is how the prototype's screens show
    /// a buddy. Enough to know who you are paired with, and not a full name to
    /// take away from the session.
    /// </remarks>
    public async Task<IReadOnlyList<BuddyRequestView>> RequestsForAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        await context.BuddyRequests
            .AsNoTracking()
            .Where(request => request.RequesterId == learnerId || request.RecipientId == learnerId)
            .OrderByDescending(request => request.CreatedAt)
            .Select(request => new BuddyRequestView(
                request.Id,
                request.Status,
                request.RequesterId == learnerId ? "sent" : "received",
                request.RequesterId == learnerId
                    ? request.Recipient.ReferenceCode
                    : request.Requester.ReferenceCode,
                request.RequesterId == learnerId
                    ? request.Recipient.FirstName
                    : request.Requester.FirstName,
                request.RequesterId == learnerId
                    ? request.Recipient.Surname.Substring(0, 1)
                    : request.Requester.Surname.Substring(0, 1),
                request.RequesterId == learnerId
                    ? request.Recipient.Grade
                    : request.Requester.Grade,
                request.RequesterId == learnerId
                    ? request.Recipient.Suburb
                    : request.Requester.Suburb,
                request.CreatedAt))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The staff approval queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Both children appear by pseudonym only.</strong> A staff member
    /// approving a pairing needs to know that two Grade 9 learners want to study
    /// together; they do not need a list of named children on screen to decide
    /// it, and the reference codes are what the rest of the safeguarding process
    /// uses anyway.
    /// </para>
    /// <para>
    /// Oldest first. A request that has been waiting a week is the one holding up
    /// two children who asked to work together.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<PendingPairing>> PendingApprovalsAsync(
        CancellationToken cancellationToken = default) =>
        await context.BuddyRequests
            .AsNoTracking()
            .Where(request => request.Status == BuddyRequestStatus.Pending)
            .OrderBy(request => request.CreatedAt)
            .Select(request => new PendingPairing(
                request.Id,
                request.CreatedAt,
                request.Requester.ReferenceCode,
                request.Requester.Grade,
                request.Recipient.ReferenceCode,
                request.Recipient.Grade))
            .ToListAsync(cancellationToken);
}

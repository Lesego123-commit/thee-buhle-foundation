// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>
/// Mentorship assignments and check-ins.
/// </summary>
/// <remarks>
/// The most safeguarding-sensitive repository in the system, because check-in
/// notes are where a mentor writes down what a child told them.
/// </remarks>
public sealed class MentorshipRepository(TheeBuhleDbContext context, IClock clock) : IMentorshipRepository
{
    /// <summary>
    /// Finds an assignment with the learner and mentor loaded.
    /// </summary>
    /// <remarks>
    /// The learner is included because <c>MentorshipService.LogCheckInAsync</c>
    /// needs the reference code to put on the domain event — and it needs the
    /// pseudonym rather than the name precisely so that the event, and therefore
    /// the log, never carries an identified child.
    /// </remarks>
    public Task<MentorAssignment?> FindAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        context.MentorAssignments
            .Include(assignment => assignment.Learner)
            .Include(assignment => assignment.Mentor)
            .FirstOrDefaultAsync(assignment => assignment.Id == assignmentId, cancellationToken);

    public async Task<AssignmentView?> ActiveAssignmentForAsync(
        Guid learnerId,
        int schoolYear,
        CancellationToken cancellationToken = default) =>
        await context.MentorAssignments
            .AsNoTracking()
            .Where(assignment => assignment.LearnerId == learnerId
                && assignment.SchoolYear == schoolYear
                && assignment.Status == AssignmentStatus.Active)
            .Select(assignment => new AssignmentView(
                assignment.Id,
                assignment.MentorId,
                assignment.LearnerId,
                assignment.SchoolYear,
                assignment.Mentor.FirstName + " " + assignment.Mentor.Surname,
                assignment.Mentor.Occupation))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// A mentor's mentees, with their check-in counts.
    /// </summary>
    /// <remarks>
    /// The counts are computed in the query rather than by loading the check-ins,
    /// because the mentor's home screen needs "how many and when last" and not a
    /// single note — and loading the notes to count them would pull safeguarding
    /// text into a process that has no use for it.
    /// </remarks>
    public async Task<IReadOnlyList<MenteeView>> AssignmentsForMentorAsync(
        Guid mentorId,
        CancellationToken cancellationToken = default) =>
        await context.MentorAssignments
            .AsNoTracking()
            .Where(assignment => assignment.MentorId == mentorId
                && assignment.Status == AssignmentStatus.Active)
            .Select(assignment => new MenteeView(
                assignment.Id,
                assignment.LearnerId,
                assignment.Learner.ReferenceCode,
                assignment.Learner.FirstName,
                assignment.Learner.Surname,
                assignment.Learner.Grade,
                assignment.Learner.Suburb,
                assignment.CheckIns.Count,
                assignment.CheckIns
                    .OrderByDescending(checkIn => checkIn.OccurredOn)
                    .Select(checkIn => (DateOnly?)checkIn.OccurredOn)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

    public Task<Mentor?> FindMentorAsync(Guid mentorId, CancellationToken cancellationToken = default) =>
        context.Mentors.FirstOrDefaultAsync(mentor => mentor.Id == mentorId, cancellationToken);

    public void AddAssignment(MentorAssignment assignment) => context.MentorAssignments.Add(assignment);

    public void AddCheckIn(CheckIn checkIn) => context.CheckIns.Add(checkIn);

    /// <summary>
    /// Check-ins for one learner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The visibility rule is a SQL predicate, not a filter applied
    /// afterwards.</strong> When <paramref name="includeFlagged"/> is false — which
    /// it is for every caller except the safeguarding lead — a flagged note is
    /// never read out of the database at all.
    /// </para>
    /// <para>
    /// That distinction is the whole point. Fetching every check-in and then
    /// removing the flagged ones in C# gives the same <em>response</em>, but it
    /// means the note text has been loaded into a process that is about to render
    /// a parent's screen. A serialiser change, a debug log, an exception dump or
    /// one forgotten <c>.Where()</c> in a refactor then leaks it. Filtering in
    /// the query means there is nothing to leak.
    /// </para>
    /// <para>
    /// Task 1 section 9.3.13.7. Not to the parent, not to the learner, not to
    /// ordinary programme staff — and not to the mentor who wrote it either,
    /// because once flagged, a concern belongs to the safeguarding process.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<CheckInView>> CheckInsForLearnerAsync(
        Guid learnerId,
        int limit,
        bool includeFlagged,
        CancellationToken cancellationToken = default)
    {
        var checkIns = context.CheckIns
            .AsNoTracking()
            .Where(checkIn => checkIn.Assignment.LearnerId == learnerId);

        if (!includeFlagged)
        {
            checkIns = checkIns.Where(checkIn => !checkIn.ConcernFlagged);
        }

        return await checkIns
            .OrderByDescending(checkIn => checkIn.OccurredOn)
            .Take(limit)
            .Select(checkIn => new CheckInView(
                checkIn.Id,
                checkIn.OccurredOn,
                checkIn.Mode,
                checkIn.Wellbeing,
                checkIn.Notes,
                checkIn.SupportAreas,
                checkIn.ConcernFlagged,
                checkIn.EscalatedAt,
                checkIn.Assignment.Mentor.FirstName + " " + checkIn.Assignment.Mentor.Surname,
                checkIn.Assignment.Mentor.Occupation))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The safeguarding lead's queue: open flagged concerns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Open" means flagged and not yet closed. Ordered oldest first, which is
    /// the opposite of most lists in this system and deliberately so — a concern
    /// that has been waiting nine days is the one that needs attention, and a
    /// newest-first queue buries it.
    /// </para>
    /// <para>
    /// The learner appears by <em>reference code</em>, and the projection carries
    /// no name. The safeguarding lead can look the child up; the queue itself
    /// does not put a list of identified children on a screen.
    /// </para>
    /// <para>
    /// There is exactly one caller, and the authorisation check sits in front of
    /// it in <c>AccessPolicy.CanReadFlaggedConcerns</c>.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ConcernView>> OpenConcernsAsync(
        CancellationToken cancellationToken = default) =>
        await context.CheckIns
            .AsNoTracking()
            .Where(checkIn => checkIn.ConcernFlagged && checkIn.ClosedAt == null)
            .OrderBy(checkIn => checkIn.OccurredOn)
            .Select(checkIn => new ConcernView(
                checkIn.Id,
                checkIn.OccurredOn,
                checkIn.Wellbeing,
                checkIn.SupportAreas,
                checkIn.Notes,
                checkIn.EscalatedTo,
                checkIn.EscalatedAt,
                checkIn.Assignment.Learner.ReferenceCode,
                checkIn.Assignment.Learner.Grade,
                checkIn.Assignment.Learner.Suburb,
                checkIn.Assignment.Mentor.FirstName + " " + checkIn.Assignment.Mentor.Surname))
            .ToListAsync(cancellationToken);

    public Task<int> CountCheckInsForAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        context.CheckIns
            .AsNoTracking()
            .CountAsync(checkIn => checkIn.AssignmentId == assignmentId, cancellationToken);

    /// <summary>
    /// Records that a flagged concern reached a named person.
    /// </summary>
    /// <remarks>
    /// Runs inside the same transaction as the check-in itself, so a saved
    /// flagged check-in with no escalation recorded is a state the database
    /// cannot hold. That is precisely why <c>EscalateFlaggedConcern</c> is not an
    /// advisory listener: if this cannot be written, the check-in must not exist.
    /// </remarks>
    public async Task MarkEscalatedAsync(
        Guid checkInId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default) =>
        await context.CheckIns
            .Where(checkIn => checkIn.Id == checkInId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(checkIn => checkIn.EscalatedTo, recipientUserId)
                    .SetProperty(checkIn => checkIn.EscalatedAt, clock.Now)
                    .SetProperty(checkIn => checkIn.UpdatedAt, clock.Now),
                cancellationToken);

    /// <summary>
    /// Closes a flagged concern with a recorded outcome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Conditional on the concern still being open, which is what makes a
    /// double-submitted form a no-op rather than an overwrite of the first
    /// person's recorded outcome — and in a two-person safeguarding team, two
    /// people opening the queue at once is the normal case.
    /// </para>
    /// <para>
    /// An update, never a delete. The concern is the record.
    /// </para>
    /// </remarks>
    public async Task<bool> CloseConcernAsync(
        Guid checkInId,
        Guid closedBy,
        string outcome,
        CancellationToken cancellationToken = default)
    {
        var affected = await context.CheckIns
            .Where(checkIn => checkIn.Id == checkInId
                && checkIn.ConcernFlagged
                && checkIn.ClosedAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(checkIn => checkIn.ClosedAt, clock.Now)
                    .SetProperty(checkIn => checkIn.ClosedBy, closedBy)
                    .SetProperty(checkIn => checkIn.Outcome, outcome)
                    .SetProperty(checkIn => checkIn.UpdatedBy, closedBy)
                    .SetProperty(checkIn => checkIn.UpdatedAt, clock.Now),
                cancellationToken);

        return affected > 0;
    }
}

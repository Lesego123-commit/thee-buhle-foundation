// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Mentorship: assigning a mentor to a learner, and logging a weekly check-in.
/// </summary>
/// <remarks>
/// The rule that shapes this class is a legal one. South African law requires
/// anyone working with children to be screened against the National Child
/// Protection Register, and the schema records that as a clearance status and an
/// expiry date. A CHECK constraint can insist that a cleared mentor has an
/// expiry date, but it cannot compare that date to today — so the <em>expiry</em>
/// half of the rule is enforced here, and no assignment can be created without
/// passing through it.
/// </remarks>
public sealed class MentorshipService(
    IMentorshipRepository mentorships,
    IAuditRepository audit,
    IDomainEventDispatcher events,
    IUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock)
{
    /// <summary>
    /// Assigns a mentor to a learner for the current school year.
    /// </summary>
    public async Task<Guid> AssignAsync(
        AuthenticatedUser actor,
        Guid mentorId,
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        var mentor = await mentorships.FindMentorAsync(mentorId, cancellationToken)
            ?? throw new DomainRuleException("That mentor does not exist.");

        AssertCleared(mentor);

        var schoolYear = clock.Today.Year;

        if (await mentorships.ActiveAssignmentForAsync(learnerId, schoolYear, cancellationToken) is not null)
        {
            // Handing a child between mentors mid-year is a decision somebody
            // has to make explicitly, so the existing assignment must be ended
            // first. The database also refuses this through a filtered unique
            // index; catching it here means the caller gets a sentence rather
            // than a constraint violation.
            throw new DomainRuleException(
                "That learner already has an active mentor for this school year. "
                + "End the current assignment before creating a new one.");
        }

        return await unitOfWork.TransactionAsync(async () =>
        {
            var assignment = new MentorAssignment
            {
                MentorId = mentorId,
                LearnerId = learnerId,
                SchoolYear = schoolYear,
                Status = AssignmentStatus.Active,
                ApprovedBy = actor.UserId,
                UpdatedBy = actor.UserId,
            };

            mentorships.AddAssignment(assignment);

            audit.Write(
                actor.UserId,
                "mentorship.assigned",
                nameof(MentorAssignment),
                assignment.Id.ToString(),
                before: null,
                after: new { MentorId = mentorId, LearnerId = learnerId, SchoolYear = schoolYear },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return assignment.Id;
        }, cancellationToken);
    }

    /// <summary>
    /// Logs one check-in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the concern is flagged this raises <see cref="ConcernFlagged"/>,
    /// whose listener runs the escalation chain. That listener is <em>not</em>
    /// advisory, so if the concern cannot be routed to anybody the transaction
    /// rolls back and the check-in is not saved — deliberately, because a mentor
    /// believing a concern was reported when it reached nobody is the worst
    /// failure this system could have.
    /// </para>
    /// <para>
    /// Note also that a <see cref="WellbeingRating.Concerned"/> rating flags the
    /// concern whether or not the mentor ticked the box. A mentor should not be
    /// able to record that they are worried about a child and have it sit quietly
    /// in a note.
    /// </para>
    /// </remarks>
    public async Task<CheckInResult> LogCheckInAsync(
        AuthenticatedUser actor,
        Guid assignmentId,
        DateOnly occurredOn,
        CheckInMode mode,
        WellbeingRating wellbeing,
        string? notes,
        IReadOnlyList<string> supportAreas,
        bool concernFlagged,
        CancellationToken cancellationToken = default)
    {
        var assignment = await mentorships.FindAssignmentAsync(assignmentId, cancellationToken)
            ?? throw new DomainRuleException("That mentorship assignment does not exist.");

        if (assignment.Status != AssignmentStatus.Active)
        {
            throw new DomainRuleException("That mentorship assignment is no longer active.");
        }

        if (occurredOn > clock.Today)
        {
            throw new DomainRuleException("A check-in cannot be dated in the future.");
        }

        // The shared vocabulary from SupportAreas, not a local allow-list.
        // A local one is how "abuse" came to be classified urgent by the event
        // and unrecognised here — so the tag was dropped before the event could
        // act on it, and the most serious concern a mentor can raise was never
        // escalated.
        var areas = SupportAreas.Clean(supportAreas);

        var learnerReference = assignment.Learner.ReferenceCode;

        // Build the event first, because its own IsUrgent rule is what decides
        // whether this is a safeguarding matter — and the rating alone is enough.
        var concern = new ConcernFlagged(
            CheckInId: Guid.Empty,
            AssignmentId: assignmentId,
            LearnerId: assignment.LearnerId,
            LearnerReference: learnerReference,
            MentorId: assignment.MentorId,
            Wellbeing: wellbeing,
            SupportAreas: areas);

        var flagged = concernFlagged || concern.IsUrgent;

        return await unitOfWork.TransactionAsync(async () =>
        {
            var checkIn = new CheckIn
            {
                AssignmentId = assignmentId,
                OccurredOn = occurredOn,
                Mode = mode,
                Wellbeing = wellbeing,
                Notes = notes,
                SupportAreas = areas,
                ConcernFlagged = flagged,
                UpdatedBy = actor.UserId,
            };

            mentorships.AddCheckIn(checkIn);

            // The audit row records that a check-in happened and how the learner
            // was rated. The note text is not copied here — it exists once, in
            // the check-in, where the visibility rules apply to it.
            audit.Write(
                actor.UserId,
                flagged ? "mentorship.checkin_flagged" : "mentorship.checkin_logged",
                nameof(CheckIn),
                checkIn.Id.ToString(),
                before: null,
                after: new
                {
                    AssignmentId = assignmentId,
                    LearnerReference = learnerReference,
                    Wellbeing = wellbeing,
                    Mode = mode,
                },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (flagged)
            {
                await events.DispatchAsync(concern with { CheckInId = checkIn.Id }, cancellationToken);

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new CheckInResult(checkIn.Id, flagged);
        }, cancellationToken);
    }

    /// <summary>
    /// Refuses an assignment unless the mentor's clearance is current.
    /// </summary>
    /// <remarks>
    /// The expiry comparison is the half the database cannot do, which is the
    /// whole reason this method exists rather than trusting the CHECK constraint.
    /// </remarks>
    private void AssertCleared(Mentor mentor)
    {
        if (mentor.ClearanceStatus != ClearanceStatus.Cleared)
        {
            throw new DomainRuleException(
                "That mentor is not cleared to work with children. "
                + "A mentor must hold a valid police clearance before being assigned to a learner.");
        }

        if (mentor.ClearanceExpiry is not { } expiry)
        {
            throw new DomainRuleException("That mentor's clearance has no expiry date on record.");
        }

        if (expiry <= clock.Today)
        {
            throw new DomainRuleException(
                $"That mentor's police clearance expired on {expiry:d MMMM yyyy}. "
                + "It must be renewed before a new assignment.");
        }
    }
}

public sealed record CheckInResult(Guid CheckInId, bool Escalated);

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Application.Security;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Assembles a whole dashboard screen in one call.
/// </summary>
/// <remarks>
/// <para>
/// The reason this is a service and not seven endpoints: the app opens on this
/// screen, often on an expensive and slow mobile connection in the Vaal. Seven
/// round trips to render one screen is seven chances to fail, seven TLS
/// handshakes to pay for, and seven times the data for a parent counting
/// megabytes. One endpoint that returns the whole screen is the right shape for
/// the network the users actually have.
/// </para>
/// <para>
/// Bundling the reads does <strong>not</strong> bundle the permissions. Every
/// piece still goes through <see cref="AccessPolicy"/>, and the flagged-concern
/// rule is resolved once here and passed down into the query, so a parent's
/// request never loads a safeguarding note in the first place.
/// </para>
/// </remarks>
public sealed class DashboardService(
    ILearnerRepository learners,
    IAttendanceRepository attendance,
    ISessionRepository sessions,
    ILearningRepository learning,
    IBadgeRepository badges,
    BadgeEvaluator badgeEvaluator,
    ICommunicationRepository communications,
    INotificationRepository notifications,
    IMentorshipRepository mentorships,
    AccessPolicy policy,
    IClock clock)
{
    /// <summary>
    /// How long a mentor has before a weekly check-in counts as overdue.
    /// </summary>
    /// <remarks>
    /// Eight days, not seven. Mentors commit to a weekly check-in and most do it
    /// on the Saturday; a seven-day threshold marks somebody overdue for the few
    /// hours between last Saturday and this one, which trains people to ignore
    /// the flag.
    /// </remarks>
    private const int OverdueAfterDays = 8;

    /// <summary>The parent dashboard — screen 02 of the approved prototype.</summary>
    public async Task<ParentDashboard> ForParentAsync(
        AuthenticatedUser parent,
        Guid? learnerId = null,
        CancellationToken cancellationToken = default)
    {
        var parentId = parent.ParentId
            ?? throw new DomainRuleException("This account has no parent profile.");

        var children = await learners.ForParentAsync(parentId, cancellationToken);

        if (children.Count == 0)
        {
            throw new DomainRuleException(
                "No learners are linked to this account yet. Our staff link a learner when they enrol — "
                + "ask at the Saturday session or call +27 71 000 0000.");
        }

        // Defaults to the primary guardian link, which the repository orders
        // first, so a parent with one child never has to choose.
        var selectedId = learnerId ?? children[0].LearnerId;

        if (!await policy.CanReadLearnerAsync(parent, selectedId, cancellationToken))
        {
            // Reached when a parent edits the learner id in the URL. Answered
            // with the same sentence whether the learner exists or not, so the
            // endpoint cannot be used to discover which ids are real.
            throw new DomainRuleException("You may not view that learner.");
        }

        return new ParentDashboard(
            parent.DisplayName,
            children,
            await LearnerPanelAsync(selectedId, cancellationToken),
            await notifications.UnreadCountAsync(parent.UserId, cancellationToken),
            await communications.UnreadAnnouncementCountAsync(parent.UserId, cancellationToken),
            await communications.InboxForAsync(parent.UserId, 5, cancellationToken),
            clock.Now);
    }

    /// <summary>
    /// The attendance-and-progress screen (03), which a parent reaches from the
    /// dashboard and a learner reaches for themselves.
    /// </summary>
    public async Task<LearnerProgress> LearnerProgressAsync(
        AuthenticatedUser user,
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        if (!await policy.CanReadLearnerAsync(user, learnerId, cancellationToken))
        {
            throw new DomainRuleException("You may not view that learner.");
        }

        var learner = await learners.FindAsync(learnerId, cancellationToken)
            ?? throw new DomainRuleException("That learner does not exist.");

        var schoolYear = clock.Today.Year;

        // The visibility rule, resolved once and passed into the query. A
        // parent's request never loads a flagged note; only the safeguarding
        // lead's does. Filtering after the fetch would mean the note had already
        // been read out of the database into a process that is about to render a
        // parent's screen, which is one refactor away from a leak.
        var includeFlagged = AccessPolicy.CanReadFlaggedConcerns(user);

        return new LearnerProgress(
            learnerId,
            learner.ReferenceCode,
            learner.FirstName,
            learner.Surname,
            learner.Grade,
            learner.School,
            await attendance.SummaryForLearnerAsync(learnerId, cancellationToken),
            await attendance.CurrentStreakAsync(learnerId, cancellationToken),
            await attendance.RecentForLearnerAsync(learnerId, 12, cancellationToken),
            await learning.MarkTrendAsync(learnerId, schoolYear, cancellationToken),
            await mentorships.ActiveAssignmentForAsync(learnerId, schoolYear, cancellationToken),
            await mentorships.CheckInsForLearnerAsync(learnerId, 5, includeFlagged, cancellationToken),
            await badges.AwardedForAsync(learnerId, cancellationToken),
            await badgeEvaluator.MilestoneAsync(learnerId, cancellationToken),
            await communications.OutstandingFormsForAsync(learnerId, cancellationToken),
            clock.Now);
    }

    /// <summary>
    /// The mentor's own home screen: their mentees, and how overdue each
    /// check-in is.
    /// </summary>
    public async Task<MentorDashboard> ForMentorAsync(
        AuthenticatedUser mentor,
        CancellationToken cancellationToken = default)
    {
        var mentorId = mentor.MentorId
            ?? throw new DomainRuleException("This account has no mentor profile.");

        var assignments = await mentorships.AssignmentsForMentorAsync(mentorId, cancellationToken);
        var today = clock.Today;

        var mentees = assignments
            .Select(assignment =>
            {
                var daysSince = assignment.LastCheckInOn is { } last
                    ? today.DayNumber - last.DayNumber
                    : (int?)null;

                return new MenteeCard(
                    assignment.AssignmentId,
                    assignment.LearnerId,
                    assignment.ReferenceCode,
                    assignment.FirstName,
                    assignment.Surname,
                    assignment.Grade,
                    assignment.Suburb,
                    assignment.CheckInCount,
                    assignment.LastCheckInOn,
                    daysSince,
                    // A mentee who has never been checked in on is overdue, not
                    // unknown. A new assignment with no check-in is exactly the
                    // case that must not sit quietly at the bottom of a list.
                    IsOverdue: daysSince is null || daysSince > OverdueAfterDays);
            })
            // Longest-waiting first, so the screen is ordered by who needs
            // attention rather than by when the assignment happened to be made.
            .OrderByDescending(mentee => mentee.IsOverdue)
            .ThenByDescending(mentee => mentee.DaysSince ?? int.MaxValue)
            .ToList();

        return new MentorDashboard(mentor.DisplayName, mentees, clock.Now);
    }

    private async Task<LearnerPanel> LearnerPanelAsync(Guid learnerId, CancellationToken cancellationToken)
    {
        var learner = await learners.FindAsync(learnerId, cancellationToken)
            ?? throw new DomainRuleException("That learner does not exist.");

        var schoolYear = clock.Today.Year;

        var summary = await attendance.SummaryForLearnerAsync(learnerId, cancellationToken);
        var trend = await learning.MarkTrendAsync(learnerId, schoolYear, cancellationToken);
        var awarded = await badges.AwardedForAsync(learnerId, cancellationToken);

        // The subject to lead with is the one that has moved the most, up or
        // down — a mark that has dropped eleven points is the thing a parent
        // most needs to see, and leading with the highest mark would bury it.
        var headlineSubject = trend
            .OrderByDescending(subject => Math.Abs(subject.Improvement))
            .FirstOrDefault();

        return new LearnerPanel(
            learnerId,
            learner.FirstName,
            learner.Surname,
            learner.Grade,
            await sessions.NextForLearnerAsync(learnerId, cancellationToken),
            summary,
            summary.AttendanceRate,
            headlineSubject,
            awarded.Count,
            await attendance.RecentForLearnerAsync(learnerId, 12, cancellationToken),
            await communications.OutstandingFormsForAsync(learnerId, cancellationToken));
    }
}

public sealed record ParentDashboard(
    string DisplayName,
    IReadOnlyList<LearnerCard> Children,
    LearnerPanel Selected,
    int UnreadNotifications,
    int UnreadMessages,
    IReadOnlyList<InboxMessage> Inbox,
    DateTimeOffset AsOf);

public sealed record LearnerPanel(
    Guid LearnerId,
    string FirstName,
    string Surname,
    int Grade,
    SessionCard? NextSession,
    AttendanceSummary Attendance,
    int AttendanceRate,
    SubjectMarkTrend? HeadlineSubject,
    int BadgeCount,
    IReadOnlyList<AttendanceEntryView> RecentAttendance,
    IReadOnlyList<OutstandingForm> OutstandingForms);

public sealed record LearnerProgress(
    Guid LearnerId,
    string ReferenceCode,
    string FirstName,
    string Surname,
    int Grade,
    string? School,
    AttendanceSummary Attendance,
    int Streak,
    IReadOnlyList<AttendanceEntryView> RecentAttendance,
    IReadOnlyList<SubjectMarkTrend> Marks,
    AssignmentView? Mentor,
    IReadOnlyList<CheckInView> CheckIns,
    IReadOnlyList<AwardedBadge> Badges,
    MilestoneView Milestone,
    IReadOnlyList<OutstandingForm> OutstandingForms,
    DateTimeOffset AsOf);

public sealed record MentorDashboard(
    string DisplayName,
    IReadOnlyList<MenteeCard> Mentees,
    DateTimeOffset AsOf);

public sealed record MenteeCard(
    Guid AssignmentId,
    Guid LearnerId,
    string ReferenceCode,
    string FirstName,
    string Surname,
    int Grade,
    string? Suburb,
    int CheckInCount,
    DateOnly? LastCheckInOn,
    int? DaysSince,
    bool IsOverdue);

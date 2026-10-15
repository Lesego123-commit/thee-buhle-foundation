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

/// <summary>Tutoring sessions, and the register's state.</summary>
public sealed class SessionRepository(TheeBuhleDbContext context, IClock clock) : ISessionRepository
{
    public Task<TutoringSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        context.Sessions
            .Include(session => session.Programme)
            .Include(session => session.Venue)
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    /// <summary>
    /// The next session for one learner.
    /// </summary>
    /// <remarks>
    /// Resolved through their <em>active</em> enrolments, so a learner who
    /// withdrew from the Saturday programme is not told to turn up on Saturday.
    /// The venue address comes back with it because the app shows it on the card
    /// — a parent needs the address, not the venue's name.
    /// </remarks>
    public async Task<SessionCard?> NextForLearnerAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        return await context.Sessions
            .AsNoTracking()
            .Where(session => session.StartsAt >= now
                && session.Programme.Enrolments.Any(enrolment =>
                    enrolment.LearnerId == learnerId && enrolment.Status == EnrolmentStatus.Active))
            .OrderBy(session => session.StartsAt)
            .Select(Project)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionCard>> UpcomingAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        return await context.Sessions
            .AsNoTracking()
            .Where(session => session.StartsAt >= now)
            .OrderBy(session => session.StartsAt)
            .Take(limit)
            .Select(Project)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Recent sessions with whether a register has been captured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <em>uncaptured</em> ones are the point of this query. A session whose
    /// register was never taken is invisible in an attendance report — it looks
    /// like a week on which nobody was absent, which quietly inflates every
    /// attendance figure the foundation publishes.
    /// </para>
    /// <para>
    /// So the console surfaces it as work outstanding, with the marked count
    /// beside the expected count: "0 of 24 marked" is unmistakable in a way that
    /// a missing row is not.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<RegisterState>> RecentWithRegisterStateAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        return await context.Sessions
            .AsNoTracking()
            .Where(session => session.StartsAt < now)
            .OrderByDescending(session => session.StartsAt)
            .Take(limit)
            .Select(session => new RegisterState(
                session.Id,
                session.StartsAt,
                session.SubjectFocus,
                session.Room,
                session.Programme.Name,
                session.Venue.Name,
                session.Attendance.Count,
                session.Programme.Enrolments.Count(enrolment =>
                    enrolment.Status == EnrolmentStatus.Active)))
            .ToListAsync(cancellationToken);
    }

    public void Add(TutoringSession session) => context.Sessions.Add(session);

    /// <summary>
    /// Whether a learner is enrolled in the programme this session belongs to.
    /// </summary>
    /// <remarks>
    /// Checked before attendance is captured. Marking a learner present at a
    /// programme they are not on produces an attendance rate for a programme they
    /// never joined — and, because the rate is computed over their whole history,
    /// it quietly distorts their real one too.
    /// </remarks>
    public Task<bool> IsEnrolledInSessionProgrammeAsync(
        Guid learnerId,
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        context.Sessions
            .AsNoTracking()
            .AnyAsync(
                session => session.Id == sessionId
                    && session.Programme.Enrolments.Any(enrolment =>
                        enrolment.LearnerId == learnerId
                        && enrolment.Status == EnrolmentStatus.Active),
                cancellationToken);

    private static readonly System.Linq.Expressions.Expression<Func<TutoringSession, SessionCard>> Project =
        session => new SessionCard(
            session.Id,
            session.StartsAt,
            session.EndsAt,
            session.SubjectFocus,
            session.Room,
            session.Programme.Name,
            session.Venue.Name,
            // The address, not just the name. A parent needs to know where to
            // drop their child off, and "Sebokeng Community Hall" is not that.
            session.Venue.Address);
}

/// <summary>
/// The aggregates behind the published impact figures.
/// </summary>
/// <remarks>
/// Separate from <see cref="IContentRepository"/> on purpose. That one reads the
/// published metrics for the website; this one recomputes them, which is a
/// nightly job with a completely different cost profile. Keeping them apart
/// means a page can never accidentally take the expensive path.
/// </remarks>
public sealed class ImpactRepository(TheeBuhleDbContext context) : IImpactRepository
{
    /// <summary>
    /// Distinct learners enrolled in any term of this school year.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct on the learner, which is the whole difficulty. A learner enrolled
    /// in both the Saturday programme and the holiday camp is <em>one</em> learner
    /// supported, not two — and counting enrolment rows would overstate the
    /// headline figure by however much the programmes overlap.
    /// </para>
    /// <para>
    /// Completed enrolments count as well as active ones. A learner who finished
    /// the year was supported that year, and dropping them on 1 December would
    /// make the figure fall as the year succeeded.
    /// </para>
    /// </remarks>
    public Task<int> LearnersSupportedAsync(int schoolYear, CancellationToken cancellationToken = default)
    {
        var prefix = $"{schoolYear}-";

        return context.Enrolments
            .AsNoTracking()
            .Where(enrolment => enrolment.Term.StartsWith(prefix)
                && (enrolment.Status == EnrolmentStatus.Active
                    || enrolment.Status == EnrolmentStatus.Completed))
            .Select(enrolment => enrolment.LearnerId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public Task<int> ActiveMentorCountAsync(int schoolYear, CancellationToken cancellationToken = default) =>
        context.MentorAssignments
            .AsNoTracking()
            .Where(assignment => assignment.SchoolYear == schoolYear
                && assignment.Status == AssignmentStatus.Active)
            .Select(assignment => assignment.MentorId)
            .Distinct()
            .CountAsync(cancellationToken);

    /// <summary>
    /// Tutoring hours delivered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attended marks multiplied by <em>each session's own length</em>, so a
    /// three-hour Saturday and a two-hour workshop are not counted as the same
    /// thing. Counting sessions and assuming an average would misstate the figure
    /// by roughly a third, and this is a number the foundation publishes to
    /// funders.
    /// </para>
    /// <para>
    /// One hour of tutoring delivered to one learner. Twenty-four learners at a
    /// three-hour session is 72 hours delivered, which is the figure that
    /// describes the work done — not 3.
    /// </para>
    /// </remarks>
    public async Task<int> TutoringHoursAsync(int schoolYear, CancellationToken cancellationToken = default)
    {
        var minutes = await context.Attendance
            .AsNoTracking()
            .Where(record => (record.Status == AttendanceStatus.Present
                    || record.Status == AttendanceStatus.Late)
                && record.Session.StartsAt.Year == schoolYear)
            .SumAsync(
                record => EF.Functions.DateDiffMinute(
                    record.Session.StartsAt,
                    record.Session.EndsAt),
                cancellationToken);

        return (int)Math.Round(minutes / 60.0);
    }

    /// <summary>
    /// The percentage of this year's mentor assignments that ran to the end.
    /// </summary>
    /// <remarks>
    /// Counts <em>ended</em> assignments against all of them. Mid-year it is
    /// therefore low by construction — most assignments are still active — which
    /// is why the caller only publishes it as a footnote when it is above zero
    /// rather than presenting "4% completed the full year" in February.
    /// </remarks>
    public async Task<int> MentorRetentionAsync(int schoolYear, CancellationToken cancellationToken = default)
    {
        var totals = await context.MentorAssignments
            .AsNoTracking()
            .Where(assignment => assignment.SchoolYear == schoolYear)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                All = group.Count(),
                Ended = group.Count(assignment => assignment.Status == AssignmentStatus.Ended),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return totals is null or { All: 0 }
            ? 0
            : (int)Math.Round(100.0 * totals.Ended / totals.All);
    }

    /// <summary>
    /// Inserts or updates one published metric.
    /// </summary>
    /// <remarks>
    /// An upsert rather than delete-then-insert. A refresh that deleted first
    /// would leave the impact page briefly empty, and a refresh that failed
    /// halfway would leave it empty for good — on the page the foundation shows
    /// to funders.
    /// </remarks>
    public async Task UpsertMetricAsync(ImpactMetric metric, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metric);

        var existing = await context.ImpactMetrics.FirstOrDefaultAsync(
            row => row.MetricKey == metric.MetricKey && row.SchoolYear == metric.SchoolYear,
            cancellationToken);

        if (existing is null)
        {
            await context.ImpactMetrics.AddAsync(metric, cancellationToken);

            return;
        }

        existing.Label = metric.Label;
        existing.ValueText = metric.ValueText;
        existing.Footnote = metric.Footnote;
        existing.SortOrder = metric.SortOrder;
        existing.ComputedAt = metric.ComputedAt;
    }
}

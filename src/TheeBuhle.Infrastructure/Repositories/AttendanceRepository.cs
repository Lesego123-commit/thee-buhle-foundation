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

/// <summary>Attendance marks and the figures derived from them.</summary>
public sealed class AttendanceRepository(TheeBuhleDbContext context) : IAttendanceRepository
{
    public Task<AttendanceRecord?> FindForSessionAndLearnerAsync(
        Guid sessionId,
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        context.Attendance.FirstOrDefaultAsync(
            record => record.SessionId == sessionId && record.LearnerId == learnerId,
            cancellationToken);

    public void Add(AttendanceRecord record) => context.Attendance.Add(record);

    public async Task<IReadOnlyList<AttendanceEntryView>> RecentForLearnerAsync(
        Guid learnerId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await context.Attendance
            .AsNoTracking()
            .Where(record => record.LearnerId == learnerId)
            // By the session's own start time, not by when the row was written.
            // A register captured on Monday for Saturday's session belongs on
            // Saturday in the parent's list.
            .OrderByDescending(record => record.Session.StartsAt)
            .Take(limit)
            .Select(record => new AttendanceEntryView(
                record.Id,
                record.Status,
                record.ArrivedAt,
                record.Note,
                record.SessionId,
                record.Session.StartsAt,
                record.Session.SubjectFocus,
                record.Session.Room,
                record.Session.Venue.Name,
                record.Session.Programme.Name))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The attendance summary for one learner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One aggregate query rather than four counts. The rate is computed here
    /// rather than in the caller so that "attendance rate" has exactly one
    /// definition in the system — the figure on a parent's dashboard, the figure
    /// on a sponsor card and the figure on the public impact page are the same
    /// calculation, not three that happen to agree.
    /// </para>
    /// <para>
    /// <strong>Late counts as attended.</strong> A learner who arrived at 09:20
    /// because the taxi was late was at the session. Excluding them would
    /// understate both the child's commitment and the programme's reach, and it
    /// is the sort of quiet definitional choice that makes two reports disagree.
    /// </para>
    /// </remarks>
    public async Task<AttendanceSummary> SummaryForLearnerAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        var totals = await context.Attendance
            .AsNoTracking()
            .Where(record => record.LearnerId == learnerId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Recorded = group.Count(),
                Present = group.Count(record => record.Status == AttendanceStatus.Present),
                Late = group.Count(record => record.Status == AttendanceStatus.Late),
                Absent = group.Count(record => record.Status == AttendanceStatus.Absent),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (totals is null or { Recorded: 0 })
        {
            return AttendanceSummary.Empty;
        }

        var attended = totals.Present + totals.Late;

        return new AttendanceSummary(
            totals.Recorded,
            attended,
            totals.Late,
            totals.Absent,
            (int)Math.Round(100.0 * attended / totals.Recorded));
    }

    /// <summary>
    /// Consecutive attended sessions, counting back from the most recent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordered by the session's own start time, not by when the row was written.
    /// A register captured late on Monday for Saturday's session must not break a
    /// streak — and it would, if the ordering were by <c>created_at</c>, because
    /// the late-captured Saturday would sort after a Sunday workshop.
    /// </para>
    /// <para>
    /// An <em>excused</em> absence breaks the streak too. That is deliberate and
    /// slightly harsh: the badge is for showing up, and a streak that survives
    /// absences is not a streak. The excuse is still recorded and still visible
    /// to the parent; it just does not earn a badge.
    /// </para>
    /// <para>
    /// The walk happens in memory over a bounded window rather than in SQL. A
    /// window function could do it in one query, but the query would be hard to
    /// read for a saving of microseconds on at most a few dozen rows — and the
    /// 60-session cap means the cost cannot grow with a learner's history.
    /// </para>
    /// </remarks>
    public async Task<int> CurrentStreakAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        const int Window = 60;

        var statuses = await context.Attendance
            .AsNoTracking()
            .Where(record => record.LearnerId == learnerId)
            .OrderByDescending(record => record.Session.StartsAt)
            .Take(Window)
            .Select(record => record.Status)
            .ToListAsync(cancellationToken);

        var streak = 0;

        foreach (var status in statuses)
        {
            if (status is not (AttendanceStatus.Present or AttendanceStatus.Late))
            {
                break;
            }

            streak++;
        }

        return streak;
    }

    /// <summary>
    /// How many marks of each status exist, across the whole programme.
    /// </summary>
    /// <remarks>
    /// Feeds the published overall attendance figure. Returned as a dictionary of
    /// raw counts rather than a percentage, so the caller can publish the
    /// denominator alongside it — "94% across 4 120 recorded entries" is a claim
    /// a funder can evaluate, and "94%" alone is not.
    /// </remarks>
    public async Task<IReadOnlyDictionary<AttendanceStatus, int>> StatusTotalsAsync(
        CancellationToken cancellationToken = default) =>
        await context.Attendance
            .AsNoTracking()
            .GroupBy(record => record.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);
}

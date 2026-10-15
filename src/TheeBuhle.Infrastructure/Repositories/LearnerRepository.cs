// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>Learners, their guardians, and the register.</summary>
public sealed class LearnerRepository(TheeBuhleDbContext context) : ILearnerRepository
{
    public Task<Learner?> FindAsync(Guid learnerId, CancellationToken cancellationToken = default) =>
        context.Learners.FirstOrDefaultAsync(learner => learner.Id == learnerId, cancellationToken);

    public Task<Learner?> FindByReferenceAsync(
        string referenceCode,
        CancellationToken cancellationToken = default) =>
        context.Learners.FirstOrDefaultAsync(
            learner => learner.ReferenceCode == referenceCode,
            cancellationToken);

    /// <summary>
    /// Whether this parent is a registered guardian of this learner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The authorisation primitive the whole parent-facing API rests on, and the
    /// query is deliberately as narrow as it can be: an <c>EXISTS</c> against the
    /// guardian-link table and nothing else.
    /// </para>
    /// <para>
    /// There is no "or the learner has no other guardian", no "or the surnames
    /// match", no "or the parent created the record". A parent sees a child
    /// because a staff member created a link row — full stop. Every one of those
    /// tempting extra clauses is a way for one family to see another's child.
    /// </para>
    /// </remarks>
    public Task<bool> IsGuardianOfAsync(
        Guid parentId,
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        context.GuardianLinks
            .AsNoTracking()
            .AnyAsync(link => link.ParentId == parentId && link.LearnerId == learnerId, cancellationToken);

    /// <summary>
    /// The learners linked to one parent, primary guardian link first.
    /// </summary>
    /// <remarks>
    /// The ordering is what lets the dashboard default to a child without asking:
    /// a parent with one child never sees a chooser.
    /// </remarks>
    public async Task<IReadOnlyList<LearnerCard>> ForParentAsync(
        Guid parentId,
        CancellationToken cancellationToken = default) =>
        await context.GuardianLinks
            .AsNoTracking()
            .Where(link => link.ParentId == parentId)
            .OrderByDescending(link => link.IsPrimary)
            .ThenBy(link => link.Learner.FirstName)
            .Select(link => new LearnerCard(
                link.LearnerId,
                link.Learner.ReferenceCode,
                link.Learner.FirstName,
                link.Learner.Surname,
                link.Learner.Grade,
                link.Learner.School,
                link.Learner.Suburb,
                link.Learner.Attendance.Count == 0
                    ? 0
                    : 100 * link.Learner.Attendance.Count(record =>
                        record.Status == AttendanceStatus.Present
                        || record.Status == AttendanceStatus.Late)
                        / link.Learner.Attendance.Count,
                link.IsPrimary))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> LearnerIdsForMentorAsync(
        Guid mentorId,
        CancellationToken cancellationToken = default) =>
        await context.MentorAssignments
            .AsNoTracking()
            .Where(assignment => assignment.MentorId == mentorId
                && assignment.Status == AssignmentStatus.Active)
            .Select(assignment => assignment.LearnerId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Who should hear about this child.
    /// </summary>
    /// <remarks>
    /// Kept as its own method so that question has exactly one answer in the
    /// codebase. Three listeners and two services ask it, and if each built its
    /// own query one of them would eventually forget the active-account filter
    /// and write a notification nobody can read.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> GuardianUserIdsAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        await context.GuardianLinks
            .AsNoTracking()
            .Where(link => link.LearnerId == learnerId
                && link.Parent.User.Status == UserStatus.Active)
            .Select(link => link.Parent.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The register for one session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One query, not one per learner. A left join from the enrolled learners
    /// onto whatever attendance has already been captured, so the console can
    /// render a part-completed register — which is the normal case, because a
    /// staff member captures a register while the session is still running.
    /// </para>
    /// <para>
    /// Only <em>actively</em> enrolled learners, and only for the programme this
    /// session belongs to. A learner who withdrew last term must not appear as an
    /// absence.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<RegisterRow>> RegisterForSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await context.Sessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.ProgrammeId })
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null)
        {
            return [];
        }

        return await context.Enrolments
            .AsNoTracking()
            .Where(enrolment => enrolment.ProgrammeId == session.ProgrammeId
                && enrolment.Status == EnrolmentStatus.Active)
            .OrderBy(enrolment => enrolment.Learner.Surname)
            .ThenBy(enrolment => enrolment.Learner.FirstName)
            .Select(enrolment => new
            {
                enrolment.LearnerId,
                enrolment.Learner.ReferenceCode,
                enrolment.Learner.FirstName,
                enrolment.Learner.Surname,
                enrolment.Learner.Grade,
                Mark = context.Attendance
                    .Where(record => record.SessionId == sessionId
                        && record.LearnerId == enrolment.LearnerId)
                    .Select(record => new
                    {
                        record.Id,
                        Status = (AttendanceStatus?)record.Status,
                        record.ArrivedAt,
                        record.Note,
                    })
                    .FirstOrDefault(),
            })
            .Select(row => new RegisterRow(
                row.LearnerId,
                row.ReferenceCode,
                row.FirstName,
                row.Surname,
                row.Grade,
                row.Mark != null ? row.Mark.Id : null,
                row.Mark != null ? row.Mark.Status : null,
                row.Mark != null ? row.Mark.ArrivedAt : null,
                row.Mark != null ? row.Mark.Note : null))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The public Sponsor-a-Child cards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The projection is the privacy decision.</strong> A reference code,
    /// a grade, a suburb and an ambition line — no name, no school, no
    /// photograph, no date of birth. A visitor to the public website can see that
    /// a Grade 9 learner in Sebokeng wants to study civil engineering, and cannot
    /// identify that child.
    /// </para>
    /// <para>
    /// Learners with an active sponsorship are excluded in the <em>query</em>,
    /// not filtered afterwards. Two reasons: the page cannot be used to work out
    /// who is already sponsored, and a change to a controller cannot widen what
    /// the page shows. A rule expressed as a SQL predicate is much harder to lose
    /// than a rule expressed as a <c>.Where()</c> a caller has to remember.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<SponsorCard>> SponsorableCardsAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        await context.Learners
            .AsNoTracking()
            // "On the programme" means holding an active enrolment. There is no
            // status column on the learner, deliberately: a learner is not
            // active in the abstract, they are enrolled in a programme for a
            // term, and deriving it means the two can never disagree.
            .Where(learner => learner.Enrolments.Any(enrolment =>
                    enrolment.Status == EnrolmentStatus.Active)
                && !context.Sponsorships.Any(sponsorship =>
                    sponsorship.LearnerId == learner.Id && sponsorship.EndDate == null))
            // Longest-waiting first, so the page is fair rather than alphabetical.
            .OrderBy(learner => learner.CreatedAt)
            .Take(limit)
            .Select(learner => new SponsorCard(
                learner.ReferenceCode,
                learner.Grade,
                learner.Suburb,
                learner.Ambition,
                learner.Attendance.Count == 0
                    ? 0
                    : 100 * learner.Attendance.Count(record =>
                        record.Status == AttendanceStatus.Present
                        || record.Status == AttendanceStatus.Late)
                        / learner.Attendance.Count))
            .ToListAsync(cancellationToken);

    public async Task<Learner> CreateAsync(
        Learner learner,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(learner);

        learner.UpdatedBy = actorId;

        await context.Learners.AddAsync(learner, cancellationToken);

        return learner;
    }

    public async Task<PagedResult<LearnerCard>> SearchAsync(
        string? query,
        int? grade,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var learners = context.Learners
            .AsNoTracking()
            .Where(learner => learner.Enrolments.Any(enrolment =>
                enrolment.Status == EnrolmentStatus.Active));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();

            // EF.Functions.Like rather than Contains, so the search is one
            // translated LIKE rather than three — and so a search for "O'Brien"
            // does not need escaping in application code.
            learners = learners.Where(learner =>
                EF.Functions.Like(learner.FirstName, $"%{term}%")
                || EF.Functions.Like(learner.Surname, $"%{term}%")
                || EF.Functions.Like(learner.ReferenceCode, $"%{term}%"));
        }

        if (grade is { } wanted)
        {
            learners = learners.Where(learner => learner.Grade == wanted);
        }

        var total = await learners.CountAsync(cancellationToken);

        var items = await learners
            .OrderBy(learner => learner.Surname)
            .ThenBy(learner => learner.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(learner => new LearnerCard(
                learner.Id,
                learner.ReferenceCode,
                learner.FirstName,
                learner.Surname,
                learner.Grade,
                learner.School,
                learner.Suburb,
                learner.Attendance.Count == 0
                    ? 0
                    : 100 * learner.Attendance.Count(record =>
                        record.Status == AttendanceStatus.Present
                        || record.Status == AttendanceStatus.Late)
                        / learner.Attendance.Count,
                // Not applicable to a staff search: "primary" describes a
                // guardian link, and this list is not reached through one.
                false))
            .ToListAsync(cancellationToken);

        return new PagedResult<LearnerCard>(items, total, page, pageSize);
    }

    /// <summary>
    /// The next unused code in the A-### series.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Computed from the current maximum rather than from a counter table.
    /// Allocated inside the caller's transaction, with the unique index on the
    /// reference code making a lost race an <em>error</em> rather than a
    /// duplicate — two staff members enrolling a learner at the same moment
    /// produce one failed insert that the caller retries, not two learners
    /// sharing a pseudonym.
    /// </para>
    /// <para>
    /// A duplicate would be a genuine problem rather than a cosmetic one: the
    /// reference code is what appears in the safeguarding queue and in every log
    /// line, in place of the child's name.
    /// </para>
    /// </remarks>
    public async Task<string> NextReferenceCodeAsync(CancellationToken cancellationToken = default)
    {
        var codes = await context.Learners
            .AsNoTracking()
            .Where(learner => learner.ReferenceCode.StartsWith("A-"))
            .Select(learner => learner.ReferenceCode)
            .ToListAsync(cancellationToken);

        var highest = codes
            .Select(code => int.TryParse(
                code.AsSpan(2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number)
                ? number
                : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"A-{(highest + 1).ToString("000", CultureInfo.InvariantCulture)}";
    }

    public async Task UpsertSummaryAsync(
        LearnerSummarySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var existing = await context.LearnerSummaries
            .FirstOrDefaultAsync(summary => summary.LearnerId == snapshot.LearnerId, cancellationToken);

        if (existing is null)
        {
            await context.LearnerSummaries.AddAsync(
                new LearnerSummary
                {
                    LearnerId = snapshot.LearnerId,
                    SessionsRecorded = snapshot.SessionsRecorded,
                    SessionsAttended = snapshot.SessionsAttended,
                    SessionsLate = snapshot.SessionsLate,
                    AttendanceRate = snapshot.AttendanceRate,
                    RefreshedAt = snapshot.RefreshedAt,
                },
                cancellationToken);

            return;
        }

        // Assignment of a recomputed snapshot, not an increment. An increment
        // that runs twice — a retried event, a replayed message — is wrong
        // permanently and silently; this is idempotent by construction.
        existing.SessionsRecorded = snapshot.SessionsRecorded;
        existing.SessionsAttended = snapshot.SessionsAttended;
        existing.SessionsLate = snapshot.SessionsLate;
        existing.AttendanceRate = snapshot.AttendanceRate;
        existing.RefreshedAt = snapshot.RefreshedAt;
    }

    // -------------------------------------------------------------------------
    //  Why the attendance rate is written out three times instead of once.
    //
    //  It used to be a private static method called from each projection, which
    //  is obviously tidier. It was also WRONG, and wrong in the worst available
    //  way: EF cannot translate a method call into SQL, so it materialised the
    //  learner and ran the method client-side — against a navigation collection
    //  that the projection had not loaded. Every learner's attendance rate came
    //  back as 0.
    //
    //  It did not throw. It did not warn. The sponsor cards on the public
    //  website simply reported that every child attends nothing, and the only
    //  way it was noticed was by calling the endpoint and comparing the answer
    //  to the database.
    //
    //  Written inline, the same expression is translated into a correlated
    //  subquery and computed in SQL. An Expression<Func<Learner,int>> field
    //  would not help either: LINQ cannot compose an expression into another
    //  expression tree without an interpreter such as LinqKit, which is a
    //  dependency for three repeated lines.
    //
    //  Late counts as attended throughout. A learner who arrived at 09:20
    //  because the taxi was late was at the session.
    // -------------------------------------------------------------------------
}

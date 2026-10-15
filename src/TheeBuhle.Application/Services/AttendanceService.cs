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
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Capturing the Saturday register.
/// </summary>
/// <remarks>
/// <para>
/// This class is the clearest example of the two patterns Task 1 chose. Look at
/// what it does <em>not</em> do: it does not award badges, does not notify
/// parents and does not refresh any cached summary. It validates, writes the
/// attendance row, and raises one <see cref="AttendanceRecorded"/> per learner.
/// </para>
/// <para>
/// All of it runs inside one <see cref="IUnitOfWork.TransactionAsync{T}"/>. The
/// attendance rows, the badges the listeners award and the notification rows they
/// queue commit together or not at all — so "present but no badge" is not a state
/// the database can be left in if the request dies halfway through. There is an
/// integration test that deliberately makes a listener throw and asserts the
/// attendance row is gone.
/// </para>
/// </remarks>
public sealed class AttendanceService(
    IAttendanceRepository attendance,
    ILearnerRepository learners,
    ISessionRepository sessions,
    IAuditRepository audit,
    IDomainEventDispatcher events,
    IUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock,
    ILogger<AttendanceService> logger)
{
    /// <summary>A register with more entries than this is a client bug, not a big Saturday.</summary>
    private const int MaxBulkEntries = 400;

    /// <summary>
    /// Captures or amends one learner's attendance for one session.
    /// </summary>
    public async Task<CaptureResult> CaptureAsync(
        AuthenticatedUser actor,
        Guid sessionId,
        Guid learnerId,
        AttendanceStatus status,
        DateTimeOffset? arrivedAt,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var session = await sessions.FindAsync(sessionId, cancellationToken)
            ?? throw new DomainRuleException("That session does not exist.");

        var learner = await learners.FindAsync(learnerId, cancellationToken)
            ?? throw new DomainRuleException("That learner does not exist.");

        if (!await sessions.IsEnrolledInSessionProgrammeAsync(learnerId, sessionId, cancellationToken))
        {
            // Marking a learner present at a session they are not enrolled in
            // would silently corrupt both the learner's attendance rate and the
            // programme figures on the public Impact page.
            throw new DomainRuleException(
                "That learner is not actively enrolled in this session's programme. "
                + "Enrol them first, then capture the register.");
        }

        var arrival = NormaliseArrival(arrivedAt, status, session.StartsAt, session.EndsAt);
        var learnerReference = learner.ReferenceCode;

        return await unitOfWork.TransactionAsync(async () =>
        {
            var existing = await attendance.FindForSessionAndLearnerAsync(sessionId, learnerId, cancellationToken);
            Guid attendanceId;
            bool created;

            if (existing is not null)
            {
                var before = new { existing.Status, existing.ArrivedAt, existing.Note };

                existing.Status = status;
                existing.ArrivedAt = arrival;
                existing.Note = note;
                existing.UpdatedBy = actor.UserId;

                audit.Write(
                    actor.UserId,
                    "attendance.amended",
                    nameof(AttendanceRecord),
                    existing.Id.ToString(),
                    before,
                    new
                    {
                        Status = status,
                        ArrivedAt = arrival,
                        Note = note,
                        LearnerReference = learnerReference,
                    },
                    requestContext.IpAddress,
                    requestContext.UserAgent);

                attendanceId = existing.Id;
                created = false;
            }
            else
            {
                var record = new AttendanceRecord
                {
                    SessionId = sessionId,
                    LearnerId = learnerId,
                    Status = status,
                    ArrivedAt = arrival,
                    Note = note,
                    RecordedBy = actor.UserId,
                };

                attendance.Add(record);

                audit.Write(
                    actor.UserId,
                    "attendance.recorded",
                    nameof(AttendanceRecord),
                    record.Id.ToString(),
                    before: null,
                    // The learner's REFERENCE CODE, so the trail is readable on
                    // its own. Without it, "who was marked absent" requires
                    // joining the attendance row — which is resolvable but makes
                    // the audit log useless as a standalone record, and
                    // inconsistent with the check-in entries that do carry it.
                    //
                    // The pseudonym, never the name: this is a table nobody ever
                    // deletes from.
                    after: new
                    {
                        Status = status,
                        SessionId = sessionId,
                        LearnerReference = learnerReference,
                    },
                    requestContext.IpAddress,
                    requestContext.UserAgent);

                attendanceId = record.Id;
                created = true;
            }

            // The listeners read the attendance row, so it has to be in the
            // database before they run — but inside the same transaction, so a
            // listener failure still rolls it back.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // One event. Badges, the parent notification and the summary refresh
            // are all listeners on it, and none of them is named here.
            await events.DispatchAsync(
                new AttendanceRecorded(
                    attendanceId,
                    sessionId,
                    learnerId,
                    learner.ReferenceCode,
                    status,
                    arrival,
                    actor.UserId),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new CaptureResult(attendanceId, created);
        }, cancellationToken);
    }

    /// <summary>
    /// Captures a whole register in one transaction.
    /// </summary>
    /// <remarks>
    /// All-or-nothing is the right semantic: a partially saved register is worse
    /// than an unsaved one, because a staff member seeing 40 of 60 rows saved has
    /// no way to tell which 20 are missing.
    /// </remarks>
    public async Task<BulkCaptureResult> CaptureBulkAsync(
        AuthenticatedUser actor,
        Guid sessionId,
        IReadOnlyList<RegisterEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            throw new DomainRuleException("The register contained no entries.");
        }

        if (entries.Count > MaxBulkEntries)
        {
            throw new DomainRuleException(
                $"A register may not contain more than {MaxBulkEntries} entries in one request.");
        }

        var duplicates = entries
            .GroupBy(entry => entry.LearnerId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            // Caught here rather than letting the unique index fire halfway
            // through, so the message names the problem.
            throw new DomainRuleException(
                "The register lists the same learner more than once. Reload it and try again.");
        }

        return await unitOfWork.TransactionAsync(async () =>
        {
            var recorded = 0;
            var amended = 0;

            foreach (var entry in entries)
            {
                var result = await CaptureAsync(
                    actor,
                    sessionId,
                    entry.LearnerId,
                    entry.Status,
                    entry.ArrivedAt,
                    entry.Note,
                    cancellationToken);

                if (result.Created)
                {
                    recorded++;
                }
                else
                {
                    amended++;
                }
            }

            Log.RegisterCaptured(logger, sessionId, recorded, amended);

            return new BulkCaptureResult(recorded, amended);
        }, cancellationToken);
    }

    /// <summary>
    /// An arrival time only means something for a learner who arrived.
    /// </summary>
    /// <remarks>
    /// Also clamped to the session window: a clock-skewed device or a mistyped
    /// time must not produce an arrival two days after the session, which would
    /// then appear in the parent's timeline as a nonsense entry.
    /// </remarks>
    private DateTimeOffset? NormaliseArrival(
        DateTimeOffset? arrivedAt,
        AttendanceStatus status,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        if (!status.CountsAsAttended())
        {
            return null;
        }

        if (arrivedAt is null)
        {
            // No time given for a learner who was here: use now if the session is
            // running, otherwise the session's own start.
            var now = clock.Now;

            return now >= startsAt && now <= endsAt ? now : startsAt;
        }

        return arrivedAt.Value < startsAt ? startsAt
            : arrivedAt.Value > endsAt ? endsAt
            : arrivedAt.Value;
    }
}

public sealed record RegisterEntry(
    Guid LearnerId,
    AttendanceStatus Status,
    DateTimeOffset? ArrivedAt = null,
    string? Note = null);

public sealed record CaptureResult(Guid AttendanceId, bool Created);

public sealed record BulkCaptureResult(int Recorded, int Amended);

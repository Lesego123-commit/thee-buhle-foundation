// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Application.Listeners;

// =============================================================================
//  The three observers on AttendanceRecorded.
//
//  This file is the Observer pattern from Task 1 section 9.3.12.1 made real.
//  Capturing a Saturday register has three unrelated consequences, and
//  AttendanceService performs none of them — it raises one event and stays
//  closed to modification when a fourth consequence is added.
//
//  What is worth reading here is the *asymmetry* between them. One of the three
//  is advisory-free and one failure rolls the register back; two are advisory and
//  their failures are recorded rather than raised. That difference is a
//  statement about consequence, and getting it the wrong way round in either
//  direction is a real bug:
//
//    * a badge that fails silently leaves the learner's record wrong;
//    * a push notification that fails loudly leaves a mentor standing in a hall
//      in Sebokeng unable to save a register.
//
// =============================================================================

/// <summary>
/// Observer 1 of 3: award any badge now due.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not advisory.</strong> A badge is part of the truth about the learner
/// and belongs in the same commit as the attendance row that earned it, so a
/// failure here rolls the register entry back rather than leaving the two out of
/// step. A learner who attended and was not awarded a streak badge is a learner
/// whose record is wrong.
/// </para>
/// <para>
/// The notification that follows an award is a different matter. It goes through
/// <see cref="NotificationService"/>, which records a delivery failure rather
/// than raising one — so a dead push token cannot undo an award.
/// </para>
/// </remarks>
public sealed class AwardBadgesOnAttendance(
    BadgeEvaluator badges,
    NotificationService notifications,
    ILearnerRepository learners) : IDomainEventListener<AttendanceRecorded>
{
    public async Task HandleAsync(AttendanceRecorded domainEvent, CancellationToken cancellationToken)
    {
        // An absence cannot complete a streak or add to a session count, and
        // re-evaluating on every absent mark would run several queries per
        // learner for an answer that cannot have changed.
        if (!domainEvent.WasPresent)
        {
            return;
        }

        var awarded = await badges.EvaluateAsync(domainEvent.LearnerId, cancellationToken);

        if (awarded.Count == 0)
        {
            return;
        }

        var learner = await learners.FindAsync(domainEvent.LearnerId, cancellationToken);
        var firstName = learner?.FirstName ?? "Your learner";

        // The parent's account, not the learner's: a Grade 4 learner has no
        // login, and the parent is the one who wants to hear this.
        var guardians = await learners.GuardianUserIdsAsync(domainEvent.LearnerId, cancellationToken);

        foreach (var badge in awarded)
        {
            await notifications.NotifyManyAsync(
                guardians,
                NotificationCategory.Announcements,
                title: "New badge earned",
                body: $"{firstName} earned “{badge.Name}” — {badge.Description}",
                icon: badge.Icon ?? "✦",
                link: "/progress",
                cancellationToken);
        }
    }
}

/// <summary>
/// Observer 2 of 3: tell the parent.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Advisory.</strong> A push or SMS provider being down must not make a
/// mentor standing in a hall in Sebokeng unable to save a register.
/// </para>
/// <para>
/// Only absences and late arrivals are notified. A parent whose child attends 38
/// Saturdays does not want 38 "present" notifications — that is how people learn
/// to ignore an app, and then miss the one notice that mattered. The two
/// exceptions are the cases a parent actually needs to know about the same day.
/// </para>
/// </remarks>
public sealed class NotifyParentOfAttendance(
    NotificationService notifications,
    ILearnerRepository learners,
    EngagementOptions options) : IDomainEventListener<AttendanceRecorded>, IAdvisoryListener
{
    public async Task HandleAsync(AttendanceRecorded domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.Status is not (AttendanceStatus.Absent or AttendanceStatus.Late))
        {
            return;
        }

        var learner = await learners.FindAsync(domainEvent.LearnerId, cancellationToken);

        if (learner is null)
        {
            return;
        }

        var (icon, title, body) = domainEvent.Status == AttendanceStatus.Absent
            ? ("⚑",
               "Absence recorded",
               $"{learner.FirstName} was not marked present at today's session. "
               + $"If this is wrong, reply to this notice or call us on {options.PublicPhone}.")
            : ("⏰",
               "Late arrival recorded",
               // Said plainly, because a late arrival is usually a taxi and not
               // a problem, and a notice that reads like a reprimand makes a
               // parent defensive about something they could not control.
               $"{learner.FirstName} arrived after the session had started. "
               + "Nothing to do — this is just so you know.");

        var guardians = await learners.GuardianUserIdsAsync(domainEvent.LearnerId, cancellationToken);

        await notifications.NotifyManyAsync(
            guardians,
            NotificationCategory.SessionReminder,
            title,
            body,
            icon,
            link: "/progress",
            cancellationToken);
    }
}

/// <summary>
/// Observer 3 of 3: refresh the learner summary the mobile app reads offline.
/// </summary>
/// <remarks>
/// <para>
/// In the Task 1 design this listener projected into Cloud Firestore, whose
/// on-device cache gave the app its offline feed. That dependency is gone: the
/// projection is written to a small denormalised table here and the app caches it
/// locally instead (see <c>mobile/src/api/cache.ts</c>), which removes a second
/// data store to keep consistent and a service account to hold.
/// </para>
/// <para>
/// The property that mattered about the Firestore design is preserved: the
/// projection is <strong>one-way and derived</strong>. Nothing reads it as a
/// source of truth, so a stale or missing row degrades the app's first paint and
/// corrupts nothing. That is also precisely why this listener is advisory — and
/// it is the reason the summary is recomputed from the same query everything
/// else uses rather than incremented in place, because an increment that runs
/// twice is wrong for good.
/// </para>
/// </remarks>
public sealed class RefreshLearnerSummary(
    ILearnerRepository learners,
    IAttendanceRepository attendance,
    IClock clock,
    ILogger<RefreshLearnerSummary> logger)
    : IDomainEventListener<AttendanceRecorded>, IAdvisoryListener
{
    public async Task HandleAsync(AttendanceRecorded domainEvent, CancellationToken cancellationToken)
    {
        var summary = await attendance.SummaryForLearnerAsync(domainEvent.LearnerId, cancellationToken);

        await learners.UpsertSummaryAsync(
            new LearnerSummarySnapshot(
                domainEvent.LearnerId,
                summary.SessionsRecorded,
                summary.SessionsAttended,
                summary.SessionsLate,
                summary.AttendanceRate,
                clock.Now),
            cancellationToken);

        Log.LearnerSummaryProjected(logger, domainEvent.LearnerReference, summary.AttendanceRate);
    }
}

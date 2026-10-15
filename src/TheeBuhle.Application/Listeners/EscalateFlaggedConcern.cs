// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Safeguarding;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Application.Listeners;

/// <summary>
/// Runs the Chain of Responsibility when a mentor flags a concern.
/// </summary>
/// <remarks>
/// <para>
/// Emphatically <strong>not</strong> advisory, and this is the one place in the
/// system where that decision is worth arguing for explicitly. If the concern
/// cannot be routed to anybody, the check-in must not save. A mentor who ticks
/// "flag a concern for staff", sees a success message, and whose concern reached
/// nobody is the worst outcome this system can produce — worse than an error,
/// because the mentor stops worrying. So the failure is made loud instead:
/// <see cref="UnroutableConcernException"/> aborts the transaction and the
/// mentor is told to phone.
/// </para>
/// <para>
/// The notification to the recipient deliberately contains <em>no detail</em>.
/// Push notification text appears on a lock screen, and the substance of a
/// safeguarding concern about a child does not belong there — the phone may be
/// on a kitchen table, or shared. The recipient is told that a flagged concern
/// is waiting and where to read it, and nothing else.
/// </para>
/// </remarks>
public sealed class EscalateFlaggedConcern(
    IEscalationChain chain,
    IMentorshipRepository mentorships,
    NotificationService notifications,
    IAuditRepository audit,
    ILogger<EscalateFlaggedConcern> logger) : IDomainEventListener<ConcernFlagged>
{
    public async Task HandleAsync(ConcernFlagged domainEvent, CancellationToken cancellationToken)
    {
        // Throws UnroutableConcernException if nobody can receive it, which
        // rolls back the check-in. That is the intended behaviour, not a gap.
        var recipientUserId = await chain.RouteAsync(domainEvent, cancellationToken);

        await mentorships.MarkEscalatedAsync(domainEvent.CheckInId, recipientUserId, cancellationToken);

        await notifications.NotifyAsync(
            recipientUserId,
            NotificationCategory.Announcements,
            title: "A flagged concern needs your attention",
            // No learner name, no note text, no support areas — only the
            // pseudonym and a pointer to the queue behind authentication.
            body: $"A mentor has flagged a concern for learner {domainEvent.LearnerReference}. "
                + "Open the safeguarding queue to read it.",
            icon: "⚑",
            link: "/admin/safeguarding",
            cancellationToken);

        // The audit row records that a concern was raised and where it went, and
        // nothing about what it said. The note itself lives in one place only,
        // behind one visibility rule.
        audit.Write(
            userId: null,
            "safeguarding.concern_escalated",
            nameof(CheckIn),
            domainEvent.CheckInId.ToString(),
            before: null,
            after: new
            {
                LearnerReference = domainEvent.LearnerReference,
                RoutedTo = recipientUserId,
                Urgent = domainEvent.IsUrgent,
            },
            ipAddress: "0.0.0.0",
            userAgent: "system");

        Log.ConcernEscalated(
            logger,
            domainEvent.LearnerReference,
            recipientUserId,
            domainEvent.IsUrgent);
    }
}

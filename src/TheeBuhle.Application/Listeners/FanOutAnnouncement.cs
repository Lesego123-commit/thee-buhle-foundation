// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Application.Listeners;

/// <summary>
/// Delivers a published announcement to its audience.
/// </summary>
/// <remarks>
/// <para>Two steps, in this order and for a reason:</para>
/// <list type="number">
/// <item>
/// <strong>Write the delivery rows.</strong> These are the compliance record —
/// who the notice was delivered to and, later, who read it. Not advisory: for a
/// permission-form notice, "we told you" has to be provable, and a parent
/// disputing that they were ever asked about a trip is a conversation the
/// foundation needs records for.
/// </item>
/// <item>
/// <strong>Queue a notification per recipient</strong>, through whichever channel
/// each has chosen. This runs through <see cref="NotificationService"/>, which
/// records delivery failures instead of raising them, so a provider outage
/// cannot undo step 1.
/// </item>
/// </list>
/// </remarks>
public sealed class FanOutAnnouncement(
    ICommunicationRepository communications,
    IUserRepository users,
    NotificationService notifications) : IDomainEventListener<AnnouncementPublished>
{
    public async Task HandleAsync(AnnouncementPublished domainEvent, CancellationToken cancellationToken)
    {
        var recipients = await users.IdsForAudienceAsync(domainEvent.Audience, cancellationToken);

        if (recipients.Count == 0)
        {
            return;
        }

        await communications.AddRecipientsAsync(domainEvent.AnnouncementId, recipients, cancellationToken);

        // Action required overrides the category mapping. A permission slip due
        // on Friday is a deadline whatever it is nominally about, and a parent
        // who has muted "Programme updates" still needs to hear about it.
        var category = domainEvent.ActionRequired
            ? NotificationCategory.Deadlines
            : CategoryFor(domainEvent.Category);

        var due = domainEvent.DueDate is { } dueDate
            ? $" Due by {dueDate:d MMMM}."
            : string.Empty;

        await notifications.NotifyManyAsync(
            recipients,
            category,
            domainEvent.Title,
            domainEvent.Summary + due,
            icon: domainEvent.ActionRequired ? "⚑" : IconFor(domainEvent.Category),
            link: "/messages",
            cancellationToken);
    }

    /// <summary>
    /// Maps an announcement category onto a notification-preference category.
    /// </summary>
    /// <remarks>
    /// Without this mapping a parent who has switched "Events &amp; workshops"
    /// off would still receive event announcements, because they would arrive
    /// under a category they never set a preference for. A preference somebody
    /// set and the system quietly works around is worse than no preference at all.
    /// </remarks>
    private static NotificationCategory CategoryFor(AnnouncementCategory category) => category switch
    {
        AnnouncementCategory.Event => NotificationCategory.Events,
        AnnouncementCategory.PermissionForm => NotificationCategory.Deadlines,
        AnnouncementCategory.ProgrammeUpdate => NotificationCategory.Announcements,
        AnnouncementCategory.Safeguarding => NotificationCategory.Announcements,
        _ => NotificationCategory.Announcements,
    };

    private static string IconFor(AnnouncementCategory category) => category switch
    {
        AnnouncementCategory.Event => "\U0001F4C5",
        AnnouncementCategory.PermissionForm => "\U0001F4C4",
        AnnouncementCategory.ProgrammeUpdate => "▃",
        AnnouncementCategory.Safeguarding => "\U0001F6E1",
        _ => "✎",
    };
}

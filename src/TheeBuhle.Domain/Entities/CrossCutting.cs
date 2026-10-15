// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Domain.Common;

namespace TheeBuhle.Domain.Entities;

// =============================================================================
// Cross-cutting entities.
//
// Not drawn in the ERD because they reference almost everything — the Task 1
// documentation names them out of the diagram for the same reason.
// =============================================================================

/// <summary>
/// One immutable audit entry.
/// </summary>
/// <remarks>
/// <para>
/// Every write that touches a learner's record, a consent decision, a donation
/// or an account's status leaves a row here with the actor, the before state and
/// the after state.
/// </para>
/// <para>
/// The trail is evidence, so nothing may rewrite or erase it. The repository
/// offers a write method and reads, and no update or delete; database triggers
/// refuse UPDATE and DELETE outright. An administrator cannot edit it either,
/// which is the point.
/// </para>
/// <para>
/// Sensitive fields are redacted before a snapshot is stored — an audit log
/// holding password hashes would be a second copy of the credential store.
/// </para>
/// </remarks>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Null for a system write, e.g. a scheduled job.</summary>
    public Guid? UserId { get; set; }

    /// <summary>A dotted action name, e.g. <c>attendance.recorded</c>.</summary>
    public required string Action { get; set; }

    public required string EntityName { get; set; }

    public string? EntityId { get; set; }

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// How one user wants to be told about one category of thing.
/// </summary>
/// <remarks>
/// When a category is switched off the system stores <em>nothing at all</em> for
/// it — not "stores it but hides it". A suppressed notification that still turns
/// up in the app is not suppressed, and a parent who asked not to be told about
/// learning activity should not find twelve of them waiting.
/// </remarks>
public class NotificationPreference
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public NotificationCategory Category { get; set; }

    public DeliveryChannel Channel { get; set; } = DeliveryChannel.Push;

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// One notification, written before any external channel is attempted.
/// </summary>
/// <remarks>
/// That order matters: the row exists first, so the notice is in the app's list
/// whatever happens next. A parent whose push token has expired still finds the
/// message waiting, which is not true of a design that treats push as the
/// delivery and the database as a log of it. A failure is recorded as
/// <see cref="DeliveryStatus.Failed"/> rather than retried silently — a failure
/// visible in the data is one somebody can act on.
/// </remarks>
public class Notification : Entity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public NotificationCategory Category { get; set; }

    public string? Icon { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    /// <summary>An in-app route, e.g. <c>/progress</c>. Never an external URL.</summary>
    public string? Link { get; set; }

    public DeliveryChannel Channel { get; set; } = DeliveryChannel.InApp;

    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.Queued;

    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>A blog or news post on the public website.</summary>
public class NewsPost : Entity
{
    public required string Slug { get; set; }

    public NewsCategory Category { get; set; }

    public required string Title { get; set; }

    public required string Excerpt { get; set; }

    /// <summary>Allow-list sanitised on write, like an announcement body.</summary>
    public required string BodyHtml { get; set; }

    public required string AuthorName { get; set; }

    public int ReadMinutes { get; set; } = 4;

    public bool IsFeatured { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// An enquiry from the website's contact form.
/// </summary>
/// <remarks>
/// A <see cref="ContactTopic.Concern"/> message is routed to the designated
/// safeguarding lead's mailbox and never to the general enquiries inbox, and the
/// admin console lists concerns first. Task 1 section 9.3.13.7: concerns are
/// handled by the designated lead and not by programme staff.
/// </remarks>
public class ContactMessage : Entity
{
    public ContactTopic Topic { get; set; }

    public required string Name { get; set; }

    public string? Organisation { get; set; }

    public required string Email { get; set; }

    public string? Phone { get; set; }

    public required string Body { get; set; }

    public bool ConsentPopia { get; set; }

    public ContactStatus Status { get; set; } = ContactStatus.New;

    public Guid? HandledBy { get; set; }

    /// <summary>Kept to make automated form abuse traceable. Purged with the message.</summary>
    public string? IpAddress { get; set; }
}

/// <summary>A newsletter subscriber.</summary>
/// <remarks>
/// Subscribing an address already on the list re-activates it rather than
/// failing, so a visitor pressing Subscribe twice does not see an error. The API
/// answers identically whether the address was new, already subscribed or
/// previously unsubscribed — otherwise the form becomes a way of testing whether
/// somebody is on the foundation's list.
/// </remarks>
public class NewsletterSubscriber : Entity
{
    public required string Email { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? UnsubscribedAt { get; set; }
}

/// <summary>
/// A published headline figure for the Impact page.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <em>not</em> computed on page load. The figures are aggregated
/// nightly and written here, and the page reads them.
/// </para>
/// <para>
/// The reason is integrity before performance: these are the numbers the
/// foundation publishes to donors and funders, and a page that aggregates live
/// can show a half-captured Saturday register as a drop in attendance.
/// Publishing a computed snapshot means a figure on the website is one somebody
/// can stand behind — and <see cref="ComputedAt"/> is rendered next to it, so a
/// visitor can see when it was calculated.
/// </para>
/// </remarks>
public class ImpactMetric
{
    public required string MetricKey { get; set; }

    public int SchoolYear { get; set; }

    public required string Label { get; set; }

    /// <summary>
    /// Stored as text because these are presentation values: "87%", "9 140",
    /// "312". Formatting a number for display is a decision taken once, when it
    /// is computed, rather than repeated by every client.
    /// </summary>
    public required string ValueText { get; set; }

    public string? Footnote { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}

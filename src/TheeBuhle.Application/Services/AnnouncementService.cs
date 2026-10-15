// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Publishing announcements and permission-form notices.
/// </summary>
/// <remarks>
/// Announcement bodies are the only markup the system stores, so they are the
/// only place stored XSS is even possible — and they are sanitised here, on
/// write, against an allow-list. Task 1 section 9.3.13.5 commits to exactly
/// that, and sanitising on write is the stronger choice: the database cannot
/// contain a payload at all, so a future template that forgets to escape, or a
/// second client that renders the field differently, is not a new vulnerability.
/// </remarks>
public sealed class AnnouncementService(
    ICommunicationRepository communications,
    IAuditRepository audit,
    IDomainEventDispatcher events,
    IHtmlSanitiser sanitiser,
    IUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock)
{
    /// <summary>How much of the body becomes the push and SMS preview.</summary>
    private const int SummaryLength = 180;

    public async Task<PublishResult> PublishAsync(
        AuthenticatedUser actor,
        string title,
        string bodyHtml,
        AnnouncementCategory category,
        Audience audience,
        bool actionRequired,
        DateOnly? dueDate,
        CancellationToken cancellationToken = default)
    {
        if (actionRequired && dueDate is null)
        {
            throw new DomainRuleException(
                "An announcement that needs action must have a due date, so parents know by when.");
        }

        var clean = sanitiser.Sanitise(bodyHtml);

        if (string.IsNullOrWhiteSpace(clean))
        {
            throw new DomainRuleException("The announcement had no usable content after sanitising.");
        }

        // The push notification and the SMS get plain text, not markup.
        var plain = sanitiser.ToPlainText(clean);
        var summary = plain.Length <= SummaryLength ? plain : plain[..SummaryLength];

        return await unitOfWork.TransactionAsync(async () =>
        {
            var announcement = new Announcement
            {
                Title = title,
                BodyHtml = clean,
                Category = category,
                Audience = audience,
                ActionRequired = actionRequired,
                DueDate = dueDate,
                PublishedBy = actor.UserId,
                PublishedAt = clock.Now,
                UpdatedBy = actor.UserId,
            };

            communications.AddAnnouncement(announcement);

            audit.Write(
                actor.UserId,
                "announcement.published",
                nameof(Announcement),
                announcement.Id.ToString(),
                before: null,
                after: new { Category = category, Audience = audience, ActionRequired = actionRequired },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            // The fan-out listener writes the delivery rows and queues one
            // notification per recipient. Neither is named here.
            await events.DispatchAsync(
                new AnnouncementPublished(
                    announcement.Id,
                    title,
                    summary,
                    category,
                    audience,
                    actionRequired,
                    dueDate,
                    actor.UserId),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            // The fan-out listener has written the delivery rows by now, so
            // this is the real audience size. Asking the actor's own unread
            // count here would report a different number entirely.
            var recipients = await communications.CountRecipientsAsync(announcement.Id, cancellationToken);

            return new PublishResult(announcement.Id, recipients);
        }, cancellationToken);
    }

    /// <summary>
    /// Creates a permission form and the announcement that tells parents about
    /// it, in one transaction.
    /// </summary>
    /// <remarks>
    /// The two go together: a permission form nobody was told about is not a
    /// permission form, and an announcement about a form that does not exist
    /// sends parents to a dead link.
    /// </remarks>
    public async Task<PermissionFormResult> PublishPermissionFormAsync(
        AuthenticatedUser actor,
        string title,
        string body,
        DateOnly dueDate,
        Guid? eventId,
        Guid? programmeId,
        CancellationToken cancellationToken = default)
    {
        if (eventId is null == (programmeId is null))
        {
            // Mirrors the CHECK constraint. Caught here so the caller gets a
            // sentence rather than a constraint violation.
            throw new DomainRuleException(
                "A permission form must be attached to exactly one event or one programme.");
        }

        if (dueDate <= clock.Today)
        {
            throw new DomainRuleException(
                "The due date must be in the future — parents need time to respond.");
        }

        return await unitOfWork.TransactionAsync(async () =>
        {
            var form = new PermissionForm
            {
                EventId = eventId,
                ProgrammeId = programmeId,
                Title = title,
                Body = body,
                DueDate = dueDate,
                CreatedBy = actor.UserId,
                UpdatedBy = actor.UserId,
            };

            communications.AddPermissionForm(form);

            audit.Write(
                actor.UserId,
                "permission_form.created",
                nameof(PermissionForm),
                form.Id.ToString(),
                before: null,
                after: new { Title = title, DueDate = dueDate },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            // The form body is plain text from a staff member, so it is escaped
            // into a paragraph rather than trusted as markup.
            var announcement = await PublishAsync(
                actor,
                title,
                $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>",
                AnnouncementCategory.PermissionForm,
                Audience.Parents,
                actionRequired: true,
                dueDate,
                cancellationToken);

            return new PermissionFormResult(form.Id, announcement.AnnouncementId);
        }, cancellationToken);
    }
}

public sealed record PublishResult(Guid AnnouncementId, int Recipients);

public sealed record PermissionFormResult(Guid FormId, Guid AnnouncementId);

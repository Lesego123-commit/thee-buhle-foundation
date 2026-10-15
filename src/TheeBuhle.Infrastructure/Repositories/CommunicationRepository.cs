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

/// <summary>
/// Announcements, permission forms and the consent log.
/// </summary>
/// <remarks>
/// The consent methods here are the ones worth reading. They are the resolution
/// of a real bug, and the ordering they use is the fix.
/// </remarks>
public sealed class CommunicationRepository(TheeBuhleDbContext context, IClock clock) : ICommunicationRepository
{
    public void AddAnnouncement(Announcement announcement) => context.Announcements.Add(announcement);

    public Task<Announcement?> FindAnnouncementAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default) =>
        context.Announcements.FirstOrDefaultAsync(
            announcement => announcement.Id == announcementId,
            cancellationToken);

    /// <summary>
    /// Writes the delivery rows for an announcement's audience.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These rows are the compliance record — who a notice was delivered to and,
    /// later, who read it. For a permission-form notice, "we told you" has to be
    /// provable, so this is not advisory work: the fan-out listener writes these
    /// before it queues a single notification, and a failure here rolls the
    /// publish back.
    /// </para>
    /// <para>
    /// Existing recipients are skipped rather than duplicated, so re-running a
    /// fan-out is safe. The composite primary key would refuse the duplicate
    /// anyway; filtering first turns "the whole publish fails" into "the missing
    /// recipients are added".
    /// </para>
    /// </remarks>
    public async Task<int> AddRecipientsAsync(
        Guid announcementId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return 0;
        }

        var already = await context.MessageRecipients
            .AsNoTracking()
            .Where(recipient => recipient.AnnouncementId == announcementId
                && userIds.Contains(recipient.UserId))
            .Select(recipient => recipient.UserId)
            .ToListAsync(cancellationToken);

        var toAdd = userIds
            .Distinct()
            .Except(already)
            .Select(userId => new MessageRecipient
            {
                AnnouncementId = announcementId,
                UserId = userId,
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            await context.MessageRecipients.AddRangeAsync(toAdd, cancellationToken);
        }

        return toAdd.Count;
    }

    /// <summary>
    /// How many recipients an announcement was delivered to.
    /// </summary>
    /// <remarks>
    /// The real audience size, counted from the delivery rows. Worth having as
    /// its own method because the obvious near-miss — asking the publishing
    /// staff member for their own unread count — is a completely different
    /// number that happens to be an integer, and it would report nonsense in the
    /// console's "published to N recipients" message without ever failing.
    /// </remarks>
    public Task<int> CountRecipientsAsync(
        Guid announcementId,
        CancellationToken cancellationToken = default) =>
        context.MessageRecipients
            .AsNoTracking()
            .CountAsync(recipient => recipient.AnnouncementId == announcementId, cancellationToken);

    public async Task<IReadOnlyList<InboxMessage>> InboxForAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await context.MessageRecipients
            .AsNoTracking()
            .Where(recipient => recipient.UserId == userId)
            // Unread first, then newest. A parent opening the app sees what they
            // have not dealt with rather than what happened most recently.
            .OrderBy(recipient => recipient.ReadAt != null)
            .ThenByDescending(recipient => recipient.Announcement.PublishedAt)
            .Take(limit)
            .Select(recipient => new InboxMessage(
                recipient.AnnouncementId,
                recipient.Announcement.Title,
                recipient.Announcement.BodyHtml,
                recipient.Announcement.Category,
                recipient.Announcement.ActionRequired,
                recipient.Announcement.DueDate,
                // A delivery row only exists once the announcement has been
                // published, so this cannot actually be null here — but the
                // column is nullable to model a draft, and the fallback keeps
                // the compiler honest rather than asserting with a bang.
                recipient.Announcement.PublishedAt ?? recipient.Announcement.CreatedAt,
                recipient.ReadAt,
                // The organisation, not the individual staff member. A parent
                // needs to know the notice is official; which employee typed it
                // is internal.
                "Thee Buhle Foundation"))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Marks an announcement read for one user.
    /// </summary>
    /// <remarks>
    /// The user id is part of the <c>WHERE</c> clause, so marking somebody
    /// else's notice read affects no rows rather than being prevented by a
    /// check the caller has to remember.
    /// </remarks>
    public async Task<bool> MarkAnnouncementReadAsync(
        Guid announcementId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var affected = await context.MessageRecipients
            .Where(recipient => recipient.AnnouncementId == announcementId
                && recipient.UserId == userId
                && recipient.ReadAt == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(recipient => recipient.ReadAt, clock.Now),
                cancellationToken);

        return affected > 0;
    }

    public Task<int> UnreadAnnouncementCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.MessageRecipients
            .AsNoTracking()
            .CountAsync(
                recipient => recipient.UserId == userId && recipient.ReadAt == null,
                cancellationToken);

    public void AddPermissionForm(PermissionForm form) => context.PermissionForms.Add(form);

    public Task<PermissionForm?> FindPermissionFormAsync(
        Guid formId,
        CancellationToken cancellationToken = default) =>
        context.PermissionForms.FirstOrDefaultAsync(form => form.Id == formId, cancellationToken);

    /// <summary>
    /// Permission forms still awaiting a decision for one learner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolved through the <strong>latest consent by sequence</strong>, and a
    /// <em>withdrawn</em> consent correctly makes the form outstanding again.
    /// </para>
    /// <para>
    /// The obvious implementation — "does any consent row exist for this form and
    /// learner" — is wrong in exactly the case the append-only log exists to
    /// handle. A parent who granted permission and then withdrew it has two rows,
    /// so the form looks answered, and the trip list still shows their child.
    /// Asking for the latest decision instead is what makes the withdrawal mean
    /// something.
    /// </para>
    /// <para>
    /// Ordered by <c>Sequence</c>, never by <c>SignedAt</c> — see
    /// <see cref="CurrentConsentAsync"/> for why that is not interchangeable.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<OutstandingForm>> OutstandingFormsForAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        await context.PermissionForms
            .AsNoTracking()
            .Select(form => new
            {
                Form = form,
                Latest = form.Consents
                    .Where(consent => consent.LearnerId == learnerId)
                    .OrderByDescending(consent => consent.Sequence)
                    .Select(consent => (ConsentDecision?)consent.Decision)
                    .FirstOrDefault(),
            })
            .Where(row => row.Latest == null || row.Latest == ConsentDecision.Withdrawn)
            .OrderBy(row => row.Form.DueDate)
            .Select(row => new OutstandingForm(
                row.Form.Id,
                row.Form.Title,
                row.Form.Body,
                row.Form.DueDate,
                row.Form.Event != null ? row.Form.Event.Title : null,
                row.Form.Event != null ? (DateTimeOffset?)row.Form.Event.StartsAt : null,
                row.Form.Event != null ? row.Form.Event.Location : null,
                row.Form.Programme != null ? row.Form.Programme.Name : null,
                row.Latest))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Appends a consent row.
    /// </summary>
    /// <remarks>
    /// There is deliberately no update or delete anywhere in this class for
    /// consents. The log is append-only: a withdrawal is a new row, never a
    /// change to the granting one, and nothing is ever removed. POPIA requires
    /// the full history to stay reconstructible — if a parent asks "what did I
    /// agree to, and when did I change my mind?", the system must answer
    /// precisely.
    /// </remarks>
    public void AppendConsent(Consent consent) => context.Consents.Add(consent);

    /// <summary>
    /// The decision that stands right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Ordered by <c>Sequence</c>, not by <c>SignedAt</c>, and this is a
    /// bug fix rather than a preference.</strong>
    /// </para>
    /// <para>
    /// The original implementation ordered by the timestamp. It was found to be
    /// wrong by driving the live API: the stored timestamp had one-second
    /// resolution, so a parent who granted permission and then immediately
    /// withdrew it produced two rows with <em>identical</em> timestamps. The
    /// ordering tied, the query returned either row, and which decision counted
    /// as current was whatever the storage engine happened to hand back first.
    /// </para>
    /// <para>
    /// A parent tapping "withdraw" and the system still showing their child as
    /// permitted is not an acceptable coin flip. And the general lesson is worth
    /// stating: <em>a timestamp cannot order an append-only log</em>, because two
    /// events can share one however fine the resolution. A monotonic identity
    /// column always can.
    /// </para>
    /// </remarks>
    public Task<Consent?> CurrentConsentAsync(
        Guid formId,
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        context.Consents
            .AsNoTracking()
            .Where(consent => consent.FormId == formId && consent.LearnerId == learnerId)
            .OrderByDescending(consent => consent.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The full history for one form and learner, oldest first.
    /// </summary>
    /// <remarks>
    /// The method that makes the append-only design worth having: it can answer
    /// "granted on 3 September, withdrawn on 14 September, granted again on 20
    /// September" precisely, which a single mutable row cannot. Ordered by
    /// sequence, so two decisions in the same second still appear in the order
    /// they were actually made.
    /// </remarks>
    public async Task<IReadOnlyList<ConsentHistoryEntry>> ConsentHistoryAsync(
        Guid formId,
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        await context.Consents
            .AsNoTracking()
            .Where(consent => consent.FormId == formId && consent.LearnerId == learnerId)
            .OrderBy(consent => consent.Sequence)
            .Select(consent => new ConsentHistoryEntry(
                consent.Id,
                consent.Sequence,
                consent.Decision,
                consent.SignedAt,
                consent.SignatureRef,
                consent.Parent.FirstName + " " + consent.Parent.Surname))
            .ToListAsync(cancellationToken);
}

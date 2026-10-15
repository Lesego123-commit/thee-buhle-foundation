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
/// Public engagement: event registrations, volunteer applications, contact
/// messages and the newsletter list.
/// </summary>
public sealed class EngagementRepository(TheeBuhleDbContext context, IClock clock) : IEngagementRepository
{
    /// <summary>
    /// Remaining places, with the event row locked for the rest of the
    /// transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method that stops a camp with 120 places taking 121 bookings. Raw SQL
    /// rather than LINQ, because <c>UPDLOCK, HOLDLOCK</c> is the whole point and
    /// EF has no way to express a locking hint — a LINQ version of this query is
    /// a correct-looking read with none of the guarantee.
    /// </para>
    /// <para>
    /// What the two hints do: <c>UPDLOCK</c> takes an update lock, so a second
    /// request asking the same question waits rather than reading the same stale
    /// count. <c>HOLDLOCK</c> holds it to the end of the transaction rather than
    /// releasing it when the statement finishes — without it the lock is gone
    /// before the insert happens, which is precisely when it is needed.
    /// </para>
    /// <para>
    /// A capacity of zero means uncapped, and is reported as
    /// <see cref="int.MaxValue"/> so the caller's comparison works without a
    /// special case.
    /// </para>
    /// </remarks>
    public async Task<int?> RemainingPlacesForUpdateAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var capacities = await context.Database
            .SqlQuery<int>($"""
                SELECT e.capacity AS Value
                  FROM events AS e WITH (UPDLOCK, HOLDLOCK)
                 WHERE e.id = {eventId}
                """)
            .ToListAsync(cancellationToken);

        if (capacities.Count == 0)
        {
            return null;
        }

        var capacity = capacities[0];

        if (capacity == 0)
        {
            return int.MaxValue;
        }

        // People, not bookings. Read after the lock is held, so it cannot change
        // between the capacity read and the decision.
        var taken = await context.EventRegistrations
            .Where(registration => registration.EventId == eventId)
            .SumAsync(registration => registration.PartySize, cancellationToken);

        return Math.Max(0, capacity - taken);
    }

    public void AddRegistration(EventRegistration registration) =>
        context.EventRegistrations.Add(registration);

    public Task<bool> IsAlreadyRegisteredAsync(
        Guid eventId,
        string email,
        CancellationToken cancellationToken = default) =>
        context.EventRegistrations
            .AsNoTracking()
            .AnyAsync(
                registration => registration.EventId == eventId && registration.Email == email,
                cancellationToken);

    public async Task<IReadOnlyList<RegistrationView>> RegistrationsForEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default) =>
        await context.EventRegistrations
            .AsNoTracking()
            .Where(registration => registration.EventId == eventId)
            .OrderBy(registration => registration.AttendeeName)
            .Select(registration => new RegistrationView(
                registration.Id,
                registration.AttendeeName,
                registration.Email,
                registration.Phone,
                registration.PartySize,
                registration.Paid,
                registration.CreatedAt))
            .ToListAsync(cancellationToken);

    public void AddVolunteerApplication(VolunteerApplication application) =>
        context.VolunteerApplications.Add(application);

    public Task<VolunteerApplication?> FindVolunteerApplicationAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        context.VolunteerApplications
            .Include(application => application.Documents)
            .FirstOrDefaultAsync(application => application.Id == applicationId, cancellationToken);

    /// <summary>
    /// The volunteer vetting queue.
    /// </summary>
    /// <remarks>
    /// The projection carries a document <em>count</em> rather than the
    /// documents. A compliance document is a police clearance certificate — the
    /// list screen needs to know whether one has been uploaded, not to load a
    /// storage path for every applicant on the page.
    /// </remarks>
    public async Task<PagedResult<VolunteerView>> VolunteerApplicationsAsync(
        VolunteerStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var applications = context.VolunteerApplications.AsNoTracking();

        if (status is { } wanted)
        {
            applications = applications.Where(application => application.Status == wanted);
        }

        var total = await applications.CountAsync(cancellationToken);

        var items = await applications
            // Oldest first. Somebody who applied five weeks ago and has heard
            // nothing is the one the queue should surface — a newest-first
            // volunteer queue is how applicants quietly give up.
            .OrderBy(application => application.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(application => new VolunteerView(
                application.Id,
                application.FirstName + " " + application.Surname,
                application.Email,
                application.Phone,
                application.Suburb,
                application.Skills,
                application.Status,
                application.Documents.Count,
                application.SubmittedAt,
                application.ReviewedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<VolunteerView>(items, total, page, pageSize);
    }

    /// <summary>
    /// Moves an application to a new status.
    /// </summary>
    /// <remarks>
    /// Conditional on it not already being in that status, so a double-clicked
    /// Approve button records one review by one person rather than overwriting
    /// the reviewer and the timestamp.
    /// </remarks>
    public async Task<bool> SetVolunteerStatusAsync(
        Guid applicationId,
        VolunteerStatus status,
        Guid reviewerId,
        CancellationToken cancellationToken = default)
    {
        var affected = await context.VolunteerApplications
            .Where(application => application.Id == applicationId && application.Status != status)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(application => application.Status, status)
                    .SetProperty(application => application.ReviewedBy, reviewerId)
                    .SetProperty(application => application.ReviewedAt, clock.Now)
                    .SetProperty(application => application.UpdatedBy, reviewerId)
                    .SetProperty(application => application.UpdatedAt, clock.Now),
                cancellationToken);

        return affected > 0;
    }

    public void AddContactMessage(ContactMessage message) => context.ContactMessages.Add(message);

    public async Task<PagedResult<ContactView>> ContactMessagesAsync(
        ContactStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var messages = context.ContactMessages.AsNoTracking();

        if (status is { } wanted)
        {
            messages = messages.Where(message => message.Status == wanted);
        }

        var total = await messages.CountAsync(cancellationToken);

        var items = await messages
            // A child-safety enquiry first, then oldest, regardless of when it
            // arrived. The routing already emailed the safeguarding lead; this
            // makes sure the console agrees with that priority.
            .OrderByDescending(message => message.Topic == ContactTopic.Concern)
            .ThenBy(message => message.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(message => new ContactView(
                message.Id,
                message.Topic,
                message.Name,
                message.Organisation,
                message.Email,
                message.Phone,
                message.Body,
                message.Status,
                message.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ContactView>(items, total, page, pageSize);
    }

    public async Task<bool> SetContactStatusAsync(
        Guid messageId,
        ContactStatus status,
        Guid handlerId,
        CancellationToken cancellationToken = default)
    {
        var affected = await context.ContactMessages
            .Where(message => message.Id == messageId && message.Status != status)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(message => message.Status, status)
                    .SetProperty(message => message.HandledBy, handlerId)
                    .SetProperty(message => message.UpdatedBy, handlerId)
                    .SetProperty(message => message.UpdatedAt, clock.Now),
                cancellationToken);

        return affected > 0;
    }

    /// <summary>
    /// Adds an address to the newsletter list, or revives an unsubscribed one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns true only when something actually changed. That distinction is
    /// what lets the caller avoid sending a second welcome email to somebody
    /// already on the list — while still answering the HTTP request identically
    /// either way, so the form cannot be used to test whether a given address is
    /// subscribed.
    /// </para>
    /// <para>
    /// An unsubscribed address is revived rather than refused. Re-subscribing is
    /// a legitimate thing to do, and the alternative is somebody who once
    /// unsubscribed being permanently unable to opt back in.
    /// </para>
    /// </remarks>
    public async Task<bool> SubscribeToNewsletterAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.NewsletterSubscribers
            .FirstOrDefaultAsync(subscriber => subscriber.Email == email, cancellationToken);

        if (existing is null)
        {
            await context.NewsletterSubscribers.AddAsync(
                new NewsletterSubscriber
                {
                    Email = email,
                    ConfirmedAt = clock.Now,
                },
                cancellationToken);

            return true;
        }

        if (existing.UnsubscribedAt is null)
        {
            return false;
        }

        existing.UnsubscribedAt = null;
        existing.ConfirmedAt = clock.Now;

        return true;
    }

    /// <summary>
    /// Removes an address from the list.
    /// </summary>
    /// <remarks>
    /// A timestamp, not a delete. The record that somebody unsubscribed has to
    /// survive, or a later import of an old contact list would silently put them
    /// back on it — which is the exact failure POPIA's consent requirements exist
    /// to prevent.
    /// </remarks>
    public async Task<bool> UnsubscribeFromNewsletterAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var affected = await context.NewsletterSubscribers
            .Where(subscriber => subscriber.Email == email && subscriber.UnsubscribedAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(subscriber => subscriber.UnsubscribedAt, clock.Now)
                    .SetProperty(subscriber => subscriber.UpdatedAt, clock.Now),
                cancellationToken);

        return affected > 0;
    }
}

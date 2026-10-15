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
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Public event registration — the 5KM Social Run, the winter hike, the open day.
/// </summary>
/// <remarks>
/// <para>
/// The interesting problem here is a small one that matters operationally:
/// <strong>do not overbook</strong>. A four-day holiday camp has 120 places and
/// one bus; a community hall has a fire capacity. Two people registering the
/// last place at the same instant must not both succeed.
/// </para>
/// <para>
/// The fix is a locking read on the event row inside the transaction, so the
/// second request waits for the first to commit and then reads a true remaining
/// count. Checking capacity outside a transaction — which is the obvious way to
/// write this — reads the same stale number twice and overbooks, and it only
/// does so under exactly the load that matters: the morning registration opens.
/// </para>
/// </remarks>
public sealed class EventRegistrationService(
    IEngagementRepository engagement,
    IContentRepository content,
    IAuditRepository audit,
    IEmailSender emailSender,
    EngagementOptions options,
    IUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock,
    ILogger<EventRegistrationService> logger)
{
    /// <summary>
    /// A family, not a coach party. Anything larger is a booking staff want to
    /// talk about first, and it is also what a runaway script would look like.
    /// </summary>
    private const int MaxPartySize = 20;

    public async Task<RegistrationResult> RegisterAsync(
        string eventIdOrSlug,
        string attendeeName,
        string email,
        string? phone,
        int partySize,
        Guid? learnerId,
        bool consentPopia,
        CancellationToken cancellationToken = default)
    {
        // Checked here rather than only in the controller, so a second client —
        // the mobile app, a future partner integration — cannot register
        // somebody without it either. The website's checkbox is a courtesy to
        // the person filling it in; this is the rule.
        if (!consentPopia)
        {
            throw new DomainRuleException(
                "We cannot register you without your consent to hold these details. "
                + $"Tick the consent box, or call us on {options.PublicPhone} instead.");
        }

        if (partySize is < 1 or > MaxPartySize)
        {
            throw new DomainRuleException($"A booking may be for between 1 and {MaxPartySize} people.");
        }

        var view = await content.EventAsync(eventIdOrSlug, cancellationToken)
            ?? throw new DomainRuleException("That event does not exist, or registration is not open.");

        if (view.StartsAt < clock.Now)
        {
            throw new DomainRuleException("That event has already taken place.");
        }

        // Checked before the transaction so the common case — somebody who
        // forgot they had registered — gets a clear answer without taking a
        // lock. The unique index still backs it up if two of their tabs race.
        if (await engagement.IsAlreadyRegisteredAsync(view.EventId, email, cancellationToken))
        {
            throw new DomainRuleException(
                "That email address is already registered for this event. "
                + "Reply to your confirmation email if you need to change the number of people.");
        }

        return await unitOfWork.TransactionAsync(async () =>
        {
            // Locks the event row for the length of the transaction, so the
            // remaining count cannot change underneath this decision.
            var remaining = await engagement.RemainingPlacesForUpdateAsync(view.EventId, cancellationToken)
                ?? throw new DomainRuleException("That event does not exist.");

            if (remaining < partySize)
            {
                throw new DomainRuleException(remaining == 0
                    ? "That event is now full. Please watch our events page — we usually add a second date."
                    : $"Only {remaining} place{(remaining == 1 ? string.Empty : "s")} left, "
                      + $"and you asked for {partySize}.");
            }

            var registration = new EventRegistration
            {
                EventId = view.EventId,
                LearnerId = learnerId,
                AttendeeName = attendeeName,
                Email = email,
                Phone = phone,
                PartySize = partySize,
                ConsentPopia = true,
            };

            engagement.AddRegistration(registration);

            audit.Write(
                userId: null,
                "event.registration_created",
                nameof(EventRegistration),
                registration.Id.ToString(),
                before: null,
                after: new { EventId = view.EventId, PartySize = partySize },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            Log.EventRegistrationCreated(logger, registration.Id, partySize);

            return new RegistrationResult(
                registration.Id,
                view.Title,
                view.StartsAt,
                view.Capacity == 0 ? null : Math.Max(0, remaining - partySize));
        }, cancellationToken);
    }

    /// <summary>
    /// Records a volunteer application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A volunteer application is the only public form that collects an ID
    /// number, because a police clearance check needs one — and only the last
    /// four digits are kept. That is POPIA data minimisation applied at the
    /// point of collection (Task 1 section 9.3.13.6): the four digits are enough
    /// for a staff member to confirm they are holding the right certificate, and
    /// a full ID number in the database is a liability with no use.
    /// </para>
    /// <para>
    /// The truncation happens here rather than in the front end, so a client that
    /// posts a full number still cannot store one.
    /// </para>
    /// </remarks>
    public async Task<Guid> SubmitVolunteerApplicationAsync(
        string firstName,
        string surname,
        string email,
        string phone,
        string? suburb,
        string? idNumber,
        IReadOnlyList<string> skills,
        IReadOnlyDictionary<string, IReadOnlyList<string>> availability,
        string? experience,
        bool consentPopia,
        CancellationToken cancellationToken = default)
    {
        if (!consentPopia)
        {
            throw new DomainRuleException(
                "We need your permission to keep your details before we can accept an application.");
        }

        var digits = new string((idNumber ?? string.Empty).Where(char.IsDigit).ToArray());

        return await unitOfWork.TransactionAsync(async () =>
        {
            var application = new VolunteerApplication
            {
                FirstName = firstName,
                Surname = surname,
                Email = email,
                Phone = phone,
                Suburb = suburb,
                // Only the last four digits, and never the full number — not
                // even briefly, because this object is what gets persisted.
                IdNumberLast4 = digits.Length >= 4 ? digits[^4..] : null,
                Skills = [.. skills.Distinct(StringComparer.OrdinalIgnoreCase)],
                Availability = availability.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase),
                Experience = experience,
                ConsentPopia = true,
                SubmittedAt = clock.Now,
            };

            engagement.AddVolunteerApplication(application);

            audit.Write(
                userId: null,
                "volunteer.application_submitted",
                nameof(VolunteerApplication),
                application.Id.ToString(),
                before: null,
                // The applicant's name and contact details are on the
                // application row; repeating them in the audit trail would put
                // personal information in a table nobody ever deletes from.
                after: new { SkillCount = application.Skills.Count },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            Log.VolunteerApplicationReceived(logger, application.Id);

            return application.Id;
        }, cancellationToken);
    }

    /// <summary>
    /// Records a message from the contact form and puts it in front of the right
    /// staff member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The routing is the part that matters: a message whose topic is
    /// <see cref="ContactTopic.Concern"/> goes to the designated safeguarding
    /// lead's mailbox and <strong>never</strong> to the general enquiries inbox.
    /// Task 1 section 9.3.13.7 commits to concerns being handled by the
    /// designated lead rather than by programme staff, and a child-safety report
    /// landing in a shared inbox that four people read is exactly the failure
    /// that commitment exists to prevent.
    /// </para>
    /// <para>
    /// That decision lives here rather than in the controller, which is where the
    /// obvious place for it appears to be. A rule in a controller holds only for
    /// the one endpoint that calls it: the day a second caller appears — the
    /// mobile app, an admin-side form, a scheduled import — the routing has to be
    /// remembered again, and a forgotten safeguarding rule is not a bug that
    /// announces itself.
    /// </para>
    /// <para>
    /// The body is stored exactly as typed and rendered escaped. It is not
    /// sanitised, because unlike an announcement it is never markup — and
    /// stripping tags from a message that mentions <c>&lt;3</c> or a file
    /// extension would quietly corrupt what somebody wrote to us.
    /// </para>
    /// </remarks>
    public async Task<ContactResult> SubmitContactMessageAsync(
        ContactTopic topic,
        string name,
        string? organisation,
        string email,
        string? phone,
        string body,
        bool consentPopia,
        CancellationToken cancellationToken = default)
    {
        if (!consentPopia)
        {
            throw new DomainRuleException(
                "We cannot store your enquiry without your consent to hold these details. "
                + $"Tick the consent box, or call us on {options.PublicPhone} instead.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new DomainRuleException("Please tell us how we can help.");
        }

        var isConcern = topic == ContactTopic.Concern;

        var messageId = await unitOfWork.TransactionAsync(async () =>
        {
            var message = new ContactMessage
            {
                Topic = topic,
                Name = name,
                Organisation = organisation,
                Email = email,
                Phone = phone,
                Body = body,
                ConsentPopia = true,
                IpAddress = requestContext.IpAddress,
            };

            engagement.AddContactMessage(message);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return message.Id;
        }, cancellationToken);

        if (isConcern)
        {
            Log.SafeguardingEnquiryReceived(logger, messageId);
        }

        // Outside the transaction, and its result deliberately ignored. The
        // message is already saved and visible in the console, so a mail server
        // that is down must not lose somebody's report — least of all a
        // safeguarding one. The unanswered-messages view is what catches it.
        await emailSender.SendAsync(
            new EmailMessage(
                ToAddress: isConcern ? options.SafeguardingAddress : options.EnquiriesAddress,
                ToName: isConcern ? "Safeguarding lead" : "Thee Buhle Foundation",
                Subject: isConcern
                    ? $"CHILD SAFETY — new report ({messageId})"
                    : $"Website enquiry: {topic}",
                HtmlBody: $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>"
                    + $"<p>From {System.Net.WebUtility.HtmlEncode(name)} "
                    + $"&lt;{System.Net.WebUtility.HtmlEncode(email)}&gt;</p>",
                TextBody: $"{body}\n\n— {name} <{email}>",
                // Replying goes to the person who wrote in, not to the mailbox
                // the alert was delivered to.
                ReplyTo: email),
            cancellationToken);

        return new ContactResult(
            messageId,
            isConcern
                ? "Thank you. This has gone directly to our designated safeguarding lead. "
                  + $"If a child is in immediate danger, please call 10111 or the SAPS, and then call us on {options.SafeguardingPhone}."
                : "Thank you — we have your message and we answer every one, usually within two working days.");
    }

    /// <summary>
    /// Adds an address to the newsletter list.
    /// </summary>
    /// <remarks>
    /// Always reports success, whether or not the address was already there. An
    /// endpoint that answers "already subscribed" is an endpoint that confirms
    /// whether a given person supports this organisation, one address at a time.
    /// </remarks>
    public async Task SubscribeToNewsletterAsync(string email, CancellationToken cancellationToken = default)
    {
        await unitOfWork.TransactionAsync(async () =>
        {
            await engagement.SubscribeToNewsletterAsync(email, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }
}

public sealed record RegistrationResult(
    Guid RegistrationId,
    string EventTitle,
    DateTimeOffset StartsAt,
    int? PlacesLeft);

public sealed record ContactResult(Guid MessageId, string Message);

/// <summary>
/// The addresses and phone numbers the public forms route to.
/// </summary>
/// <remarks>
/// Configuration rather than constants, because the safeguarding lead is a named
/// person who will eventually be a different named person, and changing who
/// receives child-safety reports should be a deployment setting rather than a
/// code change and a release.
/// </remarks>
public sealed record EngagementOptions
{
    public required string EnquiriesAddress { get; init; }

    /// <summary>The designated safeguarding lead's mailbox. Never a shared inbox.</summary>
    public required string SafeguardingAddress { get; init; }

    public string PublicPhone { get; init; } = "+27 71 000 0000";

    public string SafeguardingPhone { get; init; } = "+27 71 000 0001";
}

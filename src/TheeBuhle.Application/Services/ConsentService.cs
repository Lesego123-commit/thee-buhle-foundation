// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Application.Security;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Recording a guardian's decision on a permission form.
/// </summary>
/// <remarks>
/// <para>
/// The one rule that governs everything here: <strong>consent is
/// append-only</strong>. A withdrawal is a new row, never an update of the
/// granted one, and nothing is ever deleted. POPIA requires the full consent
/// history to stay reconstructible — if a parent asks "what did I agree to, and
/// when did I change my mind?", the system must be able to answer precisely.
/// </para>
/// <para>That rule is enforced in three independent places, intentionally:</para>
/// <list type="bullet">
/// <item>the repository exposes an append method and no update or delete;</item>
/// <item>database triggers refuse UPDATE and DELETE on the table;</item>
/// <item>this service only ever appends.</item>
/// </list>
/// <para>
/// The signature reference is an HMAC, not an image or a typed name. What is
/// being recorded is <em>that this guardian, authenticated, made this decision at
/// this time from this address</em> — and a keyed hash over exactly those facts
/// is better evidence than a picture of a signature, which could have been
/// copied from another form.
/// </para>
/// </remarks>
public sealed class ConsentService(
    ICommunicationRepository communications,
    ILearnerRepository learners,
    IAuditRepository audit,
    IDomainEventDispatcher events,
    AccessPolicy policy,
    IUrlSigner signer,
    IUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock,
    ILogger<ConsentService> logger)
{
    public async Task<ConsentResult> RecordAsync(
        AuthenticatedUser parent,
        Guid formId,
        Guid learnerId,
        ConsentDecision decision,
        CancellationToken cancellationToken = default)
    {
        if (!await policy.CanSignConsentForAsync(parent, learnerId, cancellationToken))
        {
            // Staff cannot sign on a parent's behalf either — not even an
            // administrator. A consent record a guardian did not create is
            // worthless as evidence that they consented.
            throw new DomainRuleException(
                "Only a registered guardian of this learner may record a decision on this form.");
        }

        var form = await communications.FindPermissionFormAsync(formId, cancellationToken)
            ?? throw new DomainRuleException("That permission form does not exist.");

        var learner = await learners.FindAsync(learnerId, cancellationToken)
            ?? throw new DomainRuleException("That learner does not exist.");

        var current = await communications.CurrentConsentAsync(formId, learnerId, cancellationToken);

        if (decision == ConsentDecision.Withdrawn
            && current?.Decision != ConsentDecision.Granted)
        {
            throw new DomainRuleException("There is no active permission on this form to withdraw.");
        }

        if (decision != ConsentDecision.Withdrawn && current?.Decision == decision)
        {
            // Not worth failing hard on — a parent tapping twice should not see
            // a red message — but it must not write a duplicate row either,
            // because the history *is* the record.
            throw new DomainRuleException(
                $"You have already recorded \"{decision.ToString().ToLowerInvariant()}\" on this form. "
                + "Withdraw it first if you want to change your answer.");
        }

        // A closed form can still be *withdrawn* — a parent may change their mind
        // about an event that has not happened yet — but it cannot be newly
        // granted, because staff have already acted on the headcount.
        if (decision == ConsentDecision.Granted && form.DueDate < clock.Today)
        {
            throw new DomainRuleException(
                $"This form closed on {form.DueDate:d MMMM yyyy}. "
                + "Please phone us on +27 71 000 0000 — we may still be able to help.");
        }

        var parentId = parent.ParentId
            ?? throw new DomainRuleException("This account has no guardian profile.");

        var signedAt = clock.Now;

        var signature = signer.SignConsent(
            formId,
            learnerId,
            parentId,
            decision,
            signedAt,
            requestContext.IpAddress);

        return await unitOfWork.TransactionAsync(async () =>
        {
            var consent = new Consent
            {
                FormId = formId,
                LearnerId = learnerId,
                ParentId = parentId,
                Decision = decision,
                SignedAt = signedAt,
                SignatureRef = signature,
                IpAddress = requestContext.IpAddress,
                UpdatedBy = parent.UserId,
            };

            communications.AppendConsent(consent);

            audit.Write(
                parent.UserId,
                $"consent.{decision.ToString().ToLowerInvariant()}",
                nameof(Consent),
                consent.Id.ToString(),
                before: null,
                after: new
                {
                    FormId = formId,
                    LearnerReference = learner.ReferenceCode,
                    Decision = decision,
                },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            await events.DispatchAsync(
                new ConsentSigned(
                    consent.Id,
                    formId,
                    learnerId,
                    learner.ReferenceCode,
                    parentId,
                    decision),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            Log.ConsentRecorded(logger, decision, learner.ReferenceCode, formId);

            return new ConsentResult(consent.Id, decision, signature, signedAt, MessageFor(decision));
        }, cancellationToken);
    }

    /// <summary>
    /// The full history for one form and learner, oldest first.
    /// </summary>
    /// <remarks>
    /// This is the method that makes the append-only design worth having: it can
    /// answer "granted on 3 September, withdrawn on 14 September, granted again
    /// on 20 September" precisely, which a single mutable row cannot.
    /// </remarks>
    public async Task<IReadOnlyList<ConsentHistoryEntry>> HistoryAsync(
        AuthenticatedUser user,
        Guid formId,
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        if (!await policy.CanReadLearnerAsync(user, learnerId, cancellationToken))
        {
            throw new DomainRuleException("You may not read this learner's consent history.");
        }

        return await communications.ConsentHistoryAsync(formId, learnerId, cancellationToken);
    }

    private static string MessageFor(ConsentDecision decision) => decision switch
    {
        ConsentDecision.Granted =>
            "Thank you — your permission is recorded and the staff running this activity can see it.",
        ConsentDecision.Declined =>
            "Recorded. Your child will not be included in this activity.",
        ConsentDecision.Withdrawn =>
            "Your permission has been withdrawn. The staff running this activity have been told.",
        _ => "Recorded.",
    };
}

public sealed record ConsentResult(
    Guid ConsentId,
    ConsentDecision Decision,
    string SignatureRef,
    DateTimeOffset SignedAt,
    string Message);

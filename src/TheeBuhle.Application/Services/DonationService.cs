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

namespace TheeBuhle.Application.Services;

/// <summary>
/// Donations, and the payment-gateway handshake around them.
/// </summary>
/// <remarks>
/// <para>
/// The property that matters most in this class: <strong>no card data ever
/// enters this system</strong>. The front end never collects a card number.
/// What happens instead is:
/// </para>
/// <list type="number">
/// <item><see cref="InitiateAsync"/> writes a pending donation and returns a
/// signed set of form fields.</item>
/// <item>The browser posts those fields to the gateway, on the gateway's own
/// domain.</item>
/// <item>The gateway calls <see cref="HandleCallbackAsync"/> on the way back.</item>
/// </list>
/// <para>
/// So the database holds an amount, a frequency and a gateway reference, and the
/// only sensitive thing in the flow — the card — is never in scope. That is a
/// design decision rather than an omission, and it is why the donation entity has
/// a gateway reference and no payment columns at all.
/// </para>
/// </remarks>
public sealed class DonationService(
    IDonationRepository donations,
    ILearnerRepository learners,
    IAuditRepository audit,
    IDomainEventDispatcher events,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock,
    ILogger<DonationService> logger)
{
    private const decimal MinimumAmount = 10m;

    /// <summary>
    /// Not a limit on generosity — a guard against a typo or a deliberately
    /// absurd value reaching the gateway.
    /// </summary>
    private const decimal MaximumAmount = 1_000_000m;

    /// <summary>The tiers the website offers. Other amounts are accepted too.</summary>
    public static IReadOnlyList<int> SuggestedAmounts { get; } = [150, 250, 600, 1800];

    /// <summary>
    /// Creates a pending donation and returns everything the browser needs to
    /// hand the donor to the gateway.
    /// </summary>
    public async Task<CheckoutHandoff> InitiateAsync(
        string displayName,
        string email,
        decimal amount,
        DonationFrequency frequency,
        bool wantsSection18A,
        string? phone,
        string? learnerReference,
        bool consentPopia,
        CancellationToken cancellationToken = default)
    {
        // Before the amount checks, because a donor who did not consent should
        // be told that rather than being told their amount is wrong.
        if (!consentPopia)
        {
            throw new DomainRuleException(
                "We cannot process a donation without your consent to hold your details. "
                + "We need them to send your receipt and your Section 18A certificate.");
        }

        if (amount < MinimumAmount)
        {
            throw new DomainRuleException($"The smallest donation we can process is R{MinimumAmount:0}.");
        }

        if (amount > MaximumAmount)
        {
            throw new DomainRuleException(
                "For a donation of this size, please contact us directly so we can arrange it properly.");
        }

        Guid? learnerId = null;

        if (frequency == DonationFrequency.Sponsor)
        {
            if (string.IsNullOrWhiteSpace(learnerReference))
            {
                throw new DomainRuleException("Choose a learner to sponsor.");
            }

            var learner = await learners.FindByReferenceAsync(learnerReference, cancellationToken)
                ?? throw new DomainRuleException(
                    "That learner reference does not match anybody on our programme.");

            learnerId = learner.Id;

            if (await donations.ActiveSponsorshipCountAsync(learner.Id, cancellationToken) > 0)
            {
                throw new DomainRuleException(
                    "That learner has just been sponsored by somebody else. Please choose another — "
                    + "we have more learners waiting than sponsors.");
            }
        }

        return await unitOfWork.TransactionAsync(async () =>
        {
            var donor = await donations.FindOrCreateDonorAsync(email, displayName, phone, cancellationToken);

            // The reference is ours, not the gateway's, and it is unique — which
            // is what makes a duplicate callback a no-op rather than a double
            // count.
            var reference = BuildReference();

            var donation = new Donation
            {
                DonorId = donor.Id,
                Amount = amount,
                Frequency = frequency,
                // Carrying the learner id here means the sponsorship can be
                // created when the payment *completes*, not when it is started.
                Designation = learnerId is { } id ? $"sponsorship:{id}" : null,
                Status = DonationStatus.Pending,
                Gateway = gateway.GatewayName,
                GatewayReference = reference,
                WantsSection18A = wantsSection18A,
                ConsentPopia = true,
            };

            donations.AddDonation(donation);

            audit.Write(
                userId: null,
                "donation.initiated",
                nameof(Donation),
                donation.Id.ToString(),
                before: null,
                after: new { Amount = amount, Frequency = frequency },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            var fields = gateway.BuildCheckoutFields(
                new CheckoutRequest(reference, displayName, email, amount, frequency));

            return new CheckoutHandoff(donation.Id, reference, gateway.ProcessUrl, fields);
        }, cancellationToken);
    }

    /// <summary>
    /// Handles the gateway's server-to-server callback.
    /// </summary>
    /// <remarks>
    /// <para>Four checks, in this order, each closing a specific hole:</para>
    /// <list type="number">
    /// <item><strong>Signature.</strong> The payload is re-signed with the shared
    /// passphrase and compared in fixed time. Without this, anybody who can reach
    /// the endpoint can post "payment complete" and mint a donation.</item>
    ///
    /// <item><strong>Source address.</strong> The caller must be a known gateway
    /// host. A correct signature from an unexpected address means the passphrase
    /// has leaked, which is worth refusing and logging loudly.</item>
    ///
    /// <item><strong>Amount.</strong> The amount the gateway reports must match
    /// the amount we recorded. This is what stops a donor tampering with the
    /// hidden amount field in the browser and being receipted for R5 000 after
    /// paying R10.</item>
    ///
    /// <item><strong>Idempotency.</strong> Completing is conditional on the row
    /// still being pending, so a callback delivered three times — which gateways
    /// do — completes the donation once.</item>
    /// </list>
    /// </remarks>
    public async Task<CallbackOutcome> HandleCallbackAsync(
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken = default)
    {
        var reference = payload.GetValueOrDefault("m_payment_id", string.Empty);

        if (string.IsNullOrWhiteSpace(reference))
        {
            Log.GatewayCallbackRefused(logger, "missing reference", "(none)", requestContext.IpAddress);

            return new CallbackOutcome(false, "missing_reference");
        }

        if (!gateway.VerifyCallbackSignature(payload))
        {
            Log.GatewayCallbackRefused(logger, "bad signature", reference, requestContext.IpAddress);

            return new CallbackOutcome(false, "bad_signature");
        }

        if (!await gateway.IsTrustedSourceAsync(requestContext.IpAddress, cancellationToken))
        {
            Log.GatewayCallbackRefused(logger, "untrusted source", reference, requestContext.IpAddress);

            return new CallbackOutcome(false, "untrusted_source");
        }

        var donation = await donations.FindByGatewayReferenceAsync(reference, cancellationToken);

        if (donation is null)
        {
            Log.GatewayCallbackRefused(logger, "unknown donation", reference, requestContext.IpAddress);

            return new CallbackOutcome(false, "unknown_donation");
        }

        // The repository contract says the donor is loaded, because the
        // completion event carries their name and email for the receipt and the
        // tax certificate. Asserted rather than assumed: if somebody removes the
        // Include, this throws a sentence naming the cause instead of a
        // NullReferenceException from inside the payment callback, which is the
        // single worst place in this system to debug one.
        if (donation.Donor is null)
        {
            throw new InvalidOperationException(
                $"The donation {donation.Id} was loaded without its donor. "
                + $"{nameof(IDonationRepository)}.{nameof(IDonationRepository.FindByGatewayReferenceAsync)} "
                + "must include the donor — the completion event needs their name and email.");
        }

        var status = payload.GetValueOrDefault("payment_status", string.Empty);

        if (!string.Equals(status, "COMPLETE", StringComparison.OrdinalIgnoreCase))
        {
            // A declined payment is an outcome, not an error. Recorded and
            // answered 200, because a gateway retries anything else forever.
            await donations.MarkFailedAsync(
                donation.Id,
                string.IsNullOrWhiteSpace(status) ? "unknown" : status,
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new CallbackOutcome(true, "not_complete");
        }

        if (!decimal.TryParse(
                payload.GetValueOrDefault("amount_gross"),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var reported)
            || Math.Abs(Math.Round(reported, 2) - Math.Round(donation.Amount, 2)) > 0.01m)
        {
            Log.GatewayCallbackRefused(logger, "amount mismatch", reference, requestContext.IpAddress);

            await donations.MarkFailedAsync(donation.Id, "amount_mismatch", cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new CallbackOutcome(false, "amount_mismatch");
        }

        var completedAt = clock.Now;

        return await unitOfWork.TransactionAsync(async () =>
        {
            var justCompleted = await donations.MarkCompleteAsync(donation.Id, completedAt, cancellationToken);

            if (!justCompleted)
            {
                // Already complete — a redelivered callback. Answering 200 is
                // correct; gateways retry anything else.
                return new CallbackOutcome(true, "already_complete");
            }

            await CreateSponsorshipIfRequestedAsync(donation, completedAt, cancellationToken);

            audit.Write(
                userId: null,
                "donation.completed",
                nameof(Donation),
                donation.Id.ToString(),
                before: new { Status = DonationStatus.Pending },
                after: new { Status = DonationStatus.Complete, ReceivedAt = completedAt },
                requestContext.IpAddress,
                $"{gateway.GatewayName}-callback");

            await unitOfWork.SaveChangesAsync(cancellationToken);

            await events.DispatchAsync(
                new DonationCompleted(
                    donation.Id,
                    donation.DonorId,
                    donation.Donor.Email,
                    donation.Donor.DisplayName,
                    donation.Amount,
                    donation.Frequency,
                    donation.WantsSection18A),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            Log.DonationCompleted(logger, donation.Id, donation.Amount, donation.Frequency);

            return new CallbackOutcome(true, "completed");
        }, cancellationToken);
    }

    /// <summary>
    /// The "what your gift buys" copy.
    /// </summary>
    /// <remarks>
    /// Held here rather than in the front end so the website, the donation email
    /// and the app all describe a gift in the same words.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ImpactCopy { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["once"] = new Dictionary<string, string>
            {
                ["150"] = "buys a term of stationery and workbooks for two learners.",
                ["250"] = "covers one learner’s Saturday tutoring for a month.",
                ["600"] = "pays for a full term of weekly mentorship for one child.",
                ["1800"] = "sponsors one learner for a whole school year.",
            },
            ["monthly"] = new Dictionary<string, string>
            {
                ["150"] = "keeps two learners in stationery and workbooks all year.",
                ["250"] = "keeps one learner in Saturday tutoring for a full year.",
                ["600"] = "covers a full term of weekly mentorship for one child.",
                ["1800"] = "funds a place at a four-day holiday camp every school break.",
            },
            ["sponsor"] = new Dictionary<string, string>
            {
                ["150"] = "sponsors one learner for a full school year — R1 800 in total.",
                ["250"] = "sponsors one learner and covers their December camp place.",
                ["600"] = "sponsors three learners for a full school year.",
                ["1800"] = "sponsors a whole tutoring group for a term.",
            },
        };

    private async Task CreateSponsorshipIfRequestedAsync(
        Donation donation,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        const string Prefix = "sponsorship:";

        if (donation.Designation is not { } designation
            || !designation.StartsWith(Prefix, StringComparison.Ordinal)
            || !Guid.TryParse(designation[Prefix.Length..], out var learnerId))
        {
            return;
        }

        if (await donations.ActiveSponsorshipCountAsync(learnerId, cancellationToken) > 0)
        {
            // Somebody else's payment landed first. The donation stands and the
            // money is used; a staff member reassigns the sponsorship, which is
            // a conversation rather than an automatic reallocation of a child.
            Log.GatewayCallbackRefused(
                logger,
                "sponsorship target already taken — donation kept, sponsorship not created",
                donation.GatewayReference,
                requestContext.IpAddress);

            return;
        }

        donations.AddSponsorship(new Sponsorship
        {
            DonorId = donation.DonorId,
            LearnerId = learnerId,
            AnnualAmount = donation.Frequency == DonationFrequency.Monthly
                ? donation.Amount * 12
                : donation.Amount,
            StartDate = DateOnly.FromDateTime(completedAt.Date),
        });
    }

    /// <summary>e.g. TB-20260920-9F3A2C1D</summary>
    private string BuildReference() =>
        $"TB-{clock.Today:yyyyMMdd}-{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4))}";
}

public sealed record CheckoutHandoff(
    Guid DonationId,
    string GatewayReference,
    string GatewayUrl,
    IReadOnlyDictionary<string, string> Fields);

public sealed record CallbackOutcome(bool Handled, string Reason);

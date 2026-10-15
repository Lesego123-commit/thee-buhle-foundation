// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using Microsoft.Extensions.Logging.Abstractions;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.UnitTests.Fakes;

namespace TheeBuhle.UnitTests.Services;

/// <summary>
/// Donations, and the payment-gateway handshake.
/// </summary>
/// <remarks>
/// <para>
/// The callback endpoint is the most security-sensitive route in the system: a
/// public URL, unauthenticated by necessity, whose effect is to declare that the
/// foundation received money — which then appears on the public impact page and
/// produces a tax certificate a donor puts on their SARS return.
/// </para>
/// <para>
/// So these tests are written as the four checks, one at a time, each with the
/// others passing. A test that fails everything at once proves nothing about
/// which check did the work.
/// </para>
/// </remarks>
public sealed class DonationServiceTests
{
    private readonly FakeDonationRepository donations = new();

    private readonly FakeLearnerRepository learners = new();

    private readonly FakeAuditRepository audit = new();

    private readonly FakeEventDispatcher events = new();

    private readonly FakeGateway gateway = new();

    private readonly FakeUnitOfWork unitOfWork = new();

    private readonly FakeRequestContext requestContext = new();

    private readonly FakeClock clock = new();

    private readonly DonationService service;

    public DonationServiceTests() =>
        service = new DonationService(
            donations,
            learners,
            audit,
            events,
            gateway,
            unitOfWork,
            requestContext,
            clock,
            NullLogger<DonationService>.Instance);

    // ----------------------------------------------------------- initiating

    /// <summary>
    /// A donation without consent is refused, and nothing is written.
    /// </summary>
    /// <remarks>
    /// The form has always asked for this consent; until recently the API had
    /// nowhere to put the answer and no rule about it, so a client that left it
    /// out got a donation anyway. Asserting the refusal here rather than only in
    /// the controller means the mobile app and any future integration are held
    /// to the same rule.
    /// </remarks>
    [Fact]
    public async Task A_donation_without_consent_is_refused()
    {
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 250m, DonationFrequency.Once, true, null, null,
            consentPopia: false));

        Assert.Contains("consent", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(donations.Added);
    }

    /// <summary>
    /// The consent that was given is stored on the donation.
    /// </summary>
    /// <remarks>
    /// Refusing without it is only half the requirement. POPIA asks the
    /// organisation to be able to show what it was told, which means the answer
    /// has to be on the row rather than inferred from the row existing.
    /// </remarks>
    [Fact]
    public async Task A_donation_records_the_consent_that_was_given()
    {
        await service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 250m, DonationFrequency.Once, true, null, null,
            consentPopia: true);

        Assert.True(Assert.Single(donations.Added).ConsentPopia);
    }

    [Fact]
    public async Task Initiating_a_donation_creates_it_as_pending()
    {
        var handoff = await service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 250m, DonationFrequency.Once, true, null, null, consentPopia: true);

        var donation = Assert.Single(donations.Added);

        Assert.Equal(DonationStatus.Pending, donation.Status);
        Assert.Equal(250m, donation.Amount);
        Assert.NotEmpty(handoff.GatewayReference);
    }

    /// <summary>
    /// Initiating does not raise the completion event.
    /// </summary>
    /// <remarks>
    /// The whole point of the pending state. Roughly a third of donors who click
    /// through to a gateway never finish, so raising the event here would put
    /// money the foundation never received on the public impact page and would
    /// email a tax certificate for a payment that did not happen.
    /// </remarks>
    [Fact]
    public async Task Initiating_a_donation_raises_no_completion_event()
    {
        await service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 250m, DonationFrequency.Once, true, null, null, consentPopia: true);

        Assert.Empty(events.Of<DonationCompleted>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9.99)]
    public async Task A_donation_below_the_minimum_is_refused(decimal amount)
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", amount, DonationFrequency.Once, true, null, null, consentPopia: true));

        Assert.Empty(donations.Added);
    }

    /// <summary>
    /// An absurdly large amount is refused rather than sent to the gateway.
    /// </summary>
    /// <remarks>
    /// Not a limit on generosity — the message says to get in touch. It is a
    /// guard against a typo, and the message matters because refusing a large
    /// donation with "invalid amount" would lose it.
    /// </remarks>
    [Fact]
    public async Task An_amount_above_the_maximum_is_refused_with_an_invitation_to_call()
    {
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.InitiateAsync(
            "A Foundation", "grants@example.co.za", 5_000_000m, DonationFrequency.Once, true, null, null, consentPopia: true));

        Assert.Contains("contact us", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sponsoring_without_naming_a_learner_is_refused()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 1800m, DonationFrequency.Sponsor, true, null, null, consentPopia: true));
    }

    [Fact]
    public async Task Sponsoring_a_learner_who_already_has_a_sponsor_is_refused()
    {
        var learner = learners.Add("A-214");

        donations.ActiveSponsorships[learner.Id] = 1;

        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 1800m, DonationFrequency.Sponsor, true, null, "A-214", consentPopia: true));

        Assert.Contains("another", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------- check 1: the signature

    /// <summary>
    /// A bad signature is refused.
    /// </summary>
    /// <remarks>
    /// Without this check the notify endpoint is a public URL that mints
    /// donations: anybody who can reach it posts <c>payment_status=COMPLETE</c>
    /// and the published total and a tax certificate follow.
    /// </remarks>
    [Fact]
    public async Task A_callback_with_a_bad_signature_is_refused()
    {
        var donation = GivenAPendingDonation(250m);

        gateway.SignatureValid = false;

        var outcome = await service.HandleCallbackAsync(Payload(donation.GatewayReference, 250m));

        Assert.False(outcome.Handled);
        Assert.Equal("bad_signature", outcome.Reason);
        Assert.Equal(DonationStatus.Pending, donation.Status);
    }

    // ---------------------------------------------- check 2: the source address

    /// <summary>
    /// A valid signature from an unexpected address is still refused.
    /// </summary>
    /// <remarks>
    /// This is the check people leave out, on the grounds that the signature
    /// already proves authenticity. It does — which is exactly why a <em>valid</em>
    /// signature from somewhere other than the gateway means the shared
    /// passphrase has leaked, and is worth refusing and logging loudly rather
    /// than accepting.
    /// </remarks>
    [Fact]
    public async Task A_correctly_signed_callback_from_an_untrusted_address_is_refused()
    {
        var donation = GivenAPendingDonation(250m);

        gateway.SignatureValid = true;
        gateway.SourceTrusted = false;

        var outcome = await service.HandleCallbackAsync(Payload(donation.GatewayReference, 250m));

        Assert.False(outcome.Handled);
        Assert.Equal("untrusted_source", outcome.Reason);
        Assert.Equal(DonationStatus.Pending, donation.Status);
    }

    // ---------------------------------------------------- check 3: the amount

    /// <summary>
    /// A reported amount that does not match the recorded one is refused.
    /// </summary>
    /// <remarks>
    /// The attack: a donor edits the hidden amount field in their browser, pays
    /// R10, and is receipted for R5 000 — with a tax certificate to match. The
    /// signature is computed by the gateway over what the gateway received, so it
    /// is valid; only comparing against what <em>we</em> recorded catches it.
    /// </remarks>
    [Fact]
    public async Task A_callback_reporting_a_different_amount_is_refused_and_the_donation_fails()
    {
        var donation = GivenAPendingDonation(1800m);

        var outcome = await service.HandleCallbackAsync(Payload(donation.GatewayReference, 10m));

        Assert.False(outcome.Handled);
        Assert.Equal("amount_mismatch", outcome.Reason);

        // Marked failed, not left pending: this is a resolved outcome and the
        // gateway must not keep retrying it.
        Assert.Equal(DonationStatus.Failed, donation.Status);
    }

    /// <summary>
    /// A cent of rounding difference is tolerated.
    /// </summary>
    /// <remarks>
    /// Gateways format amounts to two decimal places and occasionally round the
    /// last cent differently from the way the amount was stored. Refusing a
    /// R250.00 payment reported as R249.999 would reject a genuine donation, so
    /// the comparison has a one-cent tolerance rather than being exact.
    /// </remarks>
    [Fact]
    public async Task A_cent_of_rounding_difference_is_accepted()
    {
        var donation = GivenAPendingDonation(250m);

        var outcome = await service.HandleCallbackAsync(Payload(donation.GatewayReference, 249.995m));

        Assert.True(outcome.Handled);
        Assert.Equal("completed", outcome.Reason);
    }

    // ----------------------------------------------- check 4: the idempotency

    /// <summary>
    /// A redelivered callback completes the donation once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gateways redeliver callbacks routinely — they retry anything they do not
    /// get a clean 200 for — so this is the normal case rather than an edge one.
    /// Without the conditional completion the donation is counted twice, the
    /// published total is wrong, and the donor gets a second tax certificate.
    /// </para>
    /// <para>
    /// Note also that the second delivery is <em>handled</em> rather than
    /// refused. Answering anything other than 200 makes the gateway retry
    /// forever.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_callback_delivered_three_times_completes_the_donation_once()
    {
        var donation = GivenAPendingDonation(250m);
        var payload = Payload(donation.GatewayReference, 250m);

        var first = await service.HandleCallbackAsync(payload);
        var second = await service.HandleCallbackAsync(payload);
        var third = await service.HandleCallbackAsync(payload);

        Assert.Equal("completed", first.Reason);
        Assert.Equal("already_complete", second.Reason);
        Assert.Equal("already_complete", third.Reason);

        // All three answered 200, so the gateway stops retrying.
        Assert.True(first.Handled && second.Handled && third.Handled);

        // And the completion event fired exactly once, so exactly one tax
        // certificate is issued.
        Assert.Single(events.Of<DonationCompleted>());
    }

    // -------------------------------------------------------- other outcomes

    /// <summary>
    /// A declined payment is recorded and answered 200.
    /// </summary>
    /// <remarks>
    /// A decline is an outcome, not an error. Answering anything other than 200
    /// makes the gateway retry a payment that has definitively failed.
    /// </remarks>
    [Fact]
    public async Task A_declined_payment_is_recorded_and_answered_as_handled()
    {
        var donation = GivenAPendingDonation(250m);

        var payload = Payload(donation.GatewayReference, 250m);
        payload["payment_status"] = "FAILED";

        var outcome = await service.HandleCallbackAsync(payload);

        Assert.True(outcome.Handled);
        Assert.Equal("not_complete", outcome.Reason);
        Assert.Equal(DonationStatus.Failed, donation.Status);
        Assert.Empty(events.Of<DonationCompleted>());
    }

    [Fact]
    public async Task A_callback_with_no_reference_is_refused()
    {
        var outcome = await service.HandleCallbackAsync(new Dictionary<string, string>());

        Assert.False(outcome.Handled);
        Assert.Equal("missing_reference", outcome.Reason);
    }

    [Fact]
    public async Task A_callback_for_an_unknown_reference_is_refused()
    {
        var outcome = await service.HandleCallbackAsync(Payload("TB-99999999-DEADBEEF", 250m));

        Assert.False(outcome.Handled);
        Assert.Equal("unknown_donation", outcome.Reason);
    }

    // ------------------------------------------------------------ sponsorship

    /// <summary>
    /// A sponsorship is created when the payment completes, not when it starts.
    /// </summary>
    /// <remarks>
    /// Creating it at initiation would take a child off the public sponsor page
    /// for every donor who abandoned checkout — so the page would empty out while
    /// no money arrived and no child was actually sponsored.
    /// </remarks>
    [Fact]
    public async Task A_sponsorship_is_created_only_when_the_payment_completes()
    {
        var learner = learners.Add("A-214");

        await service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 1800m, DonationFrequency.Sponsor, true, null, "A-214", consentPopia: true);

        Assert.Empty(donations.Sponsorships);

        var donation = donations.Added[0];

        await service.HandleCallbackAsync(Payload(donation.GatewayReference, 1800m));

        var sponsorship = Assert.Single(donations.Sponsorships);

        Assert.Equal(learner.Id, sponsorship.LearnerId);
    }

    /// <summary>
    /// If somebody else's payment landed first, the donation stands and the
    /// sponsorship is not created.
    /// </summary>
    /// <remarks>
    /// The money was given and is used; a staff member reassigns the
    /// sponsorship, which is a conversation rather than an automatic
    /// reallocation of a child. Refusing the donation instead would mean
    /// returning money over a race.
    /// </remarks>
    [Fact]
    public async Task If_the_learner_was_sponsored_first_the_donation_still_completes()
    {
        var learner = learners.Add("A-214");

        await service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 1800m, DonationFrequency.Sponsor, true, null, "A-214", consentPopia: true);

        // Somebody else's callback arrived in between.
        donations.ActiveSponsorships[learner.Id] = 1;

        var donation = donations.Added[0];
        var outcome = await service.HandleCallbackAsync(Payload(donation.GatewayReference, 1800m));

        Assert.True(outcome.Handled);
        Assert.Equal(DonationStatus.Complete, donation.Status);
        Assert.Empty(donations.Sponsorships);
    }

    // ---------------------------------------------------------------- privacy

    /// <summary>
    /// The donor's email is not written into the audit trail.
    /// </summary>
    /// <remarks>
    /// It is on the donation row, where it belongs. The audit trail is a table
    /// nobody ever deletes from, so putting a personal email address in it is a
    /// retention decision nobody made.
    /// </remarks>
    [Fact]
    public async Task The_audit_trail_does_not_carry_the_donors_email()
    {
        await service.InitiateAsync(
            "Thandi Mokoena", "thandi@example.co.za", 250m, DonationFrequency.Once, true, null, null, consentPopia: true);

        Assert.NotEmpty(audit.Entries);
        Assert.False(audit.AnySnapshotContains("thandi@example.co.za"));
    }

    // --------------------------------------------------------------- helpers

    private Donation GivenAPendingDonation(decimal amount)
    {
        var donor = new Donor { DisplayName = "Thandi Mokoena", Email = "thandi@example.co.za" };

        var donation = new Donation
        {
            DonorId = donor.Id,
            Donor = donor,
            Amount = amount,
            Frequency = DonationFrequency.Once,
            Status = DonationStatus.Pending,
            Gateway = "fake",
            GatewayReference = $"TB-20260920-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            WantsSection18A = true,
        };

        donations.Seed(donation);

        return donation;
    }

    private static Dictionary<string, string> Payload(string reference, decimal amount) =>
        new(StringComparer.Ordinal)
        {
            ["m_payment_id"] = reference,
            ["payment_status"] = "COMPLETE",
            ["amount_gross"] = amount.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            ["signature"] = "whatever-the-fake-accepts",
        };
}

/// <summary>A gateway whose two checks are switchable, so each can be tested alone.</summary>
internal sealed class FakeGateway : IPaymentGateway
{
    public bool SignatureValid { get; set; } = true;

    public bool SourceTrusted { get; set; } = true;

    public string GatewayName => "fake";

    public string ProcessUrl => "https://sandbox.example/process";

    public IReadOnlyDictionary<string, string> BuildCheckoutFields(CheckoutRequest request) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["m_payment_id"] = request.Reference,
            ["amount"] = request.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            ["signature"] = "fake",
        };

    public bool VerifyCallbackSignature(IReadOnlyDictionary<string, string> payload) => SignatureValid;

    public Task<bool> IsTrustedSourceAsync(string sourceIp, CancellationToken cancellationToken = default) =>
        Task.FromResult(SourceTrusted);
}

internal sealed class FakeDonationRepository : IDonationRepository
{
    private readonly Dictionary<string, Donation> byReference = [];

    public List<Donation> Added { get; } = [];

    public List<Sponsorship> Sponsorships { get; } = [];

    public Dictionary<Guid, int> ActiveSponsorships { get; } = [];

    public void Seed(Donation donation)
    {
        byReference[donation.GatewayReference] = donation;
        Added.Add(donation);
    }

    private readonly Dictionary<Guid, Donor> donors = [];

    public Task<Donor> FindOrCreateDonorAsync(string email, string displayName, string? phone, CancellationToken cancellationToken = default)
    {
        var donor = new Donor { Email = email, DisplayName = displayName, Phone = phone };

        donors[donor.Id] = donor;

        return Task.FromResult(donor);
    }

    /// <summary>
    /// Records the donation and attaches its donor.
    /// </summary>
    /// <remarks>
    /// Attaching the donor mirrors the <c>.Include</c> the real repository
    /// performs on <c>FindByGatewayReferenceAsync</c>, which the interface
    /// documents as a requirement. A fake that left the navigation null would
    /// make the service throw here for a reason that has nothing to do with the
    /// behaviour under test.
    /// </remarks>
    public void AddDonation(Donation donation)
    {
        if (donors.TryGetValue(donation.DonorId, out var donor))
        {
            donation.Donor = donor;
        }

        byReference[donation.GatewayReference] = donation;
        Added.Add(donation);
    }

    public Task<Donation?> FindByGatewayReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(byReference.GetValueOrDefault(reference));

    public Task<Donation?> FindAsync(Guid donationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Added.FirstOrDefault(d => d.Id == donationId));

    /// <summary>
    /// Conditional on the donation still being pending, exactly like the real
    /// repository's <c>WHERE status = 'Pending'</c>.
    /// </summary>
    /// <remarks>
    /// A fake that completed unconditionally would make the idempotency test
    /// pass while the production query was still unguarded — which is the one
    /// thing this fake must not do.
    /// </remarks>
    public Task<bool> MarkCompleteAsync(Guid donationId, DateTimeOffset receivedAt, CancellationToken cancellationToken = default)
    {
        var donation = Added.FirstOrDefault(d => d.Id == donationId && d.Status == DonationStatus.Pending);

        if (donation is null)
        {
            return Task.FromResult(false);
        }

        donation.Status = DonationStatus.Complete;
        donation.ReceivedAt = receivedAt;

        return Task.FromResult(true);
    }

    public Task MarkFailedAsync(Guid donationId, string reason, CancellationToken cancellationToken = default)
    {
        var donation = Added.FirstOrDefault(d => d.Id == donationId && d.Status == DonationStatus.Pending);

        if (donation is not null)
        {
            donation.Status = DonationStatus.Failed;
            donation.Designation = $"failed:{reason}";
        }

        return Task.CompletedTask;
    }

    public Task RecordSection18AIssuedAsync(Guid donationId, DateTimeOffset issuedAt, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<Donation>> AwaitingSection18AAsync(DateTimeOffset before, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Donation>>([]);

    public Task<PagedResult<DonationView>> RecentAsync(DonationStatus? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<DonationView>([], 0, page, pageSize));

    public Task<DonationTotals> TotalsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DonationTotals(0, 0, 0, 0));

    public Task<IReadOnlyList<MonthlyTotal>> MonthlyTotalsAsync(int months, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MonthlyTotal>>([]);

    public void AddSponsorship(Sponsorship sponsorship) => Sponsorships.Add(sponsorship);

    public Task<int> ActiveSponsorshipCountAsync(Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActiveSponsorships.GetValueOrDefault(learnerId));
}

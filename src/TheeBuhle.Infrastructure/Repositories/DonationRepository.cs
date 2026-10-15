// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>Donors, donations and sponsorships.</summary>
public sealed class DonationRepository(TheeBuhleDbContext context, IClock clock) : IDonationRepository
{
    /// <summary>
    /// Finds a donor by email, or creates one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deduplicated on email so a monthly giver does not accumulate one donor
    /// row a month. That is not tidiness: the Section 18A annual total is summed
    /// per donor, and a donor split across twelve rows would receive twelve
    /// certificates for a twelfth of their giving each — and would be unable to
    /// claim the deduction they are entitled to.
    /// </para>
    /// <para>
    /// The display name is refreshed on a return visit, because somebody who
    /// gave as "T Mokoena" and now gives as "Thandi Mokoena" should have the
    /// later, fuller name on their certificate.
    /// </para>
    /// </remarks>
    public async Task<Donor> FindOrCreateDonorAsync(
        string email,
        string displayName,
        string? phone,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.Donors.FirstOrDefaultAsync(
            donor => donor.Email == email,
            cancellationToken);

        if (existing is not null)
        {
            existing.DisplayName = displayName;

            if (!string.IsNullOrWhiteSpace(phone))
            {
                existing.Phone = phone;
            }

            return existing;
        }

        var donor = new Donor
        {
            Email = email,
            DisplayName = displayName,
            Phone = phone,
        };

        await context.Donors.AddAsync(donor, cancellationToken);

        return donor;
    }

    public void AddDonation(Donation donation) => context.Donations.Add(donation);

    /// <summary>
    /// Finds a donation by our gateway reference, with the donor loaded.
    /// </summary>
    /// <remarks>
    /// The donor is included rather than lazily loaded because the callback
    /// raises <c>DonationCompleted</c>, which carries the donor's name and email
    /// for the receipt and the tax certificate. A second round trip inside the
    /// transaction would be wasteful, and a null donor would be a certificate
    /// addressed to nobody.
    /// </remarks>
    public Task<Donation?> FindByGatewayReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default) =>
        context.Donations
            .Include(donation => donation.Donor)
            .FirstOrDefaultAsync(donation => donation.GatewayReference == reference, cancellationToken);

    public Task<Donation?> FindAsync(Guid donationId, CancellationToken cancellationToken = default) =>
        context.Donations
            .Include(donation => donation.Donor)
            .FirstOrDefaultAsync(donation => donation.Id == donationId, cancellationToken);

    /// <summary>
    /// Moves a pending donation to complete.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Conditional on the donation still being pending</strong>, and that
    /// condition is the idempotency guarantee. Payment gateways redeliver
    /// callbacks — routinely, not exceptionally, because they retry anything they
    /// do not get a clean 200 for — so a completion that is not conditional
    /// double-counts the donation, publishes an inflated total and issues a
    /// second tax certificate.
    /// </para>
    /// <para>
    /// Returning false rather than throwing is what lets the caller answer 200 to
    /// the duplicate. Answering anything else makes the gateway retry forever.
    /// </para>
    /// </remarks>
    public async Task<bool> MarkCompleteAsync(
        Guid donationId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        var affected = await context.Donations
            .Where(donation => donation.Id == donationId && donation.Status == DonationStatus.Pending)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(donation => donation.Status, DonationStatus.Complete)
                    .SetProperty(donation => donation.ReceivedAt, receivedAt)
                    .SetProperty(donation => donation.UpdatedAt, clock.Now),
                cancellationToken);

        return affected > 0;
    }

    /// <summary>
    /// Records that a payment did not complete.
    /// </summary>
    /// <remarks>
    /// The reason goes in the designation field, prefixed, rather than into a
    /// column of its own. A failed payment is a dead end — nothing queries the
    /// reason, and a staff member investigating one specific donation reads it
    /// once. A dedicated column would be a migration for a field with no
    /// consumer.
    /// </remarks>
    public async Task MarkFailedAsync(
        Guid donationId,
        string reason,
        CancellationToken cancellationToken = default) =>
        await context.Donations
            .Where(donation => donation.Id == donationId && donation.Status == DonationStatus.Pending)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(donation => donation.Status, DonationStatus.Failed)
                    .SetProperty(donation => donation.Designation, $"failed:{reason}")
                    .SetProperty(donation => donation.UpdatedAt, clock.Now),
                cancellationToken);

    public async Task RecordSection18AIssuedAsync(
        Guid donationId,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken = default) =>
        await context.Donations
            .Where(donation => donation.Id == donationId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(donation => donation.Section18AIssuedAt, issuedAt)
                    .SetProperty(donation => donation.UpdatedAt, clock.Now),
                cancellationToken);

    /// <summary>
    /// Donations whose certificate never went out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The retry queue. Because the certificate listener is advisory, a mail
    /// server outage leaves a completed donation with no certificate — and this
    /// query is what makes that recoverable rather than lost. A donor gets their
    /// certificate late instead of never.
    /// </para>
    /// <para>
    /// The one-hour delay the caller passes matters: without it the nightly job
    /// would race the listener and send a second certificate for a donation that
    /// completed thirty seconds ago.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<Donation>> AwaitingSection18AAsync(
        DateTimeOffset before,
        int limit,
        CancellationToken cancellationToken = default) =>
        await context.Donations
            .Include(donation => donation.Donor)
            .Where(donation => donation.Status == DonationStatus.Complete
                && donation.WantsSection18A
                && donation.Section18AIssuedAt == null
                && donation.ReceivedAt < before)
            .OrderBy(donation => donation.ReceivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<DonationView>> RecentAsync(
        DonationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var donations = context.Donations.AsNoTracking();

        if (status is { } wanted)
        {
            donations = donations.Where(donation => donation.Status == wanted);
        }

        var total = await donations.CountAsync(cancellationToken);

        var items = await donations
            .OrderByDescending(donation => donation.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(donation => new DonationView(
                donation.Id,
                donation.Donor.DisplayName,
                donation.Donor.Email,
                donation.Donor.IsOrganisation,
                donation.Amount,
                donation.Frequency,
                donation.Status,
                donation.GatewayReference,
                donation.WantsSection18A,
                donation.Section18AIssuedAt,
                donation.ReceivedAt,
                donation.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<DonationView>(items, total, page, pageSize);
    }

    /// <summary>
    /// The published totals.
    /// </summary>
    /// <remarks>
    /// <strong>Completed donations only.</strong> A pending donation is somebody
    /// who clicked through to the gateway, and roughly a third of them never
    /// finish — so including pending ones would inflate the published total by a
    /// third with money that was never received. That is not a rounding error;
    /// it is a false statement to funders.
    /// </remarks>
    public async Task<DonationTotals> TotalsAsync(CancellationToken cancellationToken = default)
    {
        var completed = context.Donations
            .AsNoTracking()
            .Where(donation => donation.Status == DonationStatus.Complete);

        var monthStart = new DateTimeOffset(
            clock.Today.Year,
            clock.Today.Month,
            1,
            0,
            0,
            0,
            TimeSpan.FromHours(2));

        var totals = await completed
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Sum(donation => donation.Amount),
                Count = group.Count(),
                MonthlyTotal = group
                    .Where(donation => donation.ReceivedAt >= monthStart)
                    .Sum(donation => donation.Amount),
                MonthlyCount = group.Count(donation => donation.ReceivedAt >= monthStart),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return totals is null
            ? new DonationTotals(0, 0, 0, 0)
            : new DonationTotals(totals.Total, totals.Count, totals.MonthlyTotal, totals.MonthlyCount);
    }

    public async Task<IReadOnlyList<MonthlyTotal>> MonthlyTotalsAsync(
        int months,
        CancellationToken cancellationToken = default)
    {
        var from = clock.Now.AddMonths(-months);

        var rows = await context.Donations
            .AsNoTracking()
            .Where(donation => donation.Status == DonationStatus.Complete
                && donation.ReceivedAt >= from)
            .GroupBy(donation => new { donation.ReceivedAt!.Value.Year, donation.ReceivedAt!.Value.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Total = group.Sum(donation => donation.Amount),
            })
            .OrderBy(row => row.Year)
            .ThenBy(row => row.Month)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new MonthlyTotal(
                $"{row.Year.ToString(CultureInfo.InvariantCulture)}-{row.Month.ToString("00", CultureInfo.InvariantCulture)}",
                row.Total))
            .ToList();
    }

    public void AddSponsorship(Sponsorship sponsorship) => context.Sponsorships.Add(sponsorship);

    /// <summary>
    /// How many active sponsorships a learner has.
    /// </summary>
    /// <remarks>
    /// Should only ever be 0 or 1 — a filtered unique index enforces that — but
    /// returning a count rather than a bool lets the caller distinguish "nobody
    /// is sponsoring this learner" from a data problem, and the sponsor page's
    /// exclusion is expressed as a count in the same query.
    /// </remarks>
    public Task<int> ActiveSponsorshipCountAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        context.Sponsorships
            .AsNoTracking()
            .CountAsync(
                sponsorship => sponsorship.LearnerId == learnerId && sponsorship.EndDate == null,
                cancellationToken);
}

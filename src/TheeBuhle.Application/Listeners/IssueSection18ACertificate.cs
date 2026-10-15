// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (1962) Income Tax Act 58 of 1962, section 18A.
// =============================================================================

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Application.Listeners;

/// <summary>
/// Emails a Section 18A tax certificate when a donation completes.
/// </summary>
/// <remarks>
/// <para>
/// Section 18A of the Income Tax Act 58 of 1962 lets a South African donor deduct a
/// donation to a registered public benefit organisation, and the certificate is
/// what SARS wants to see. The threshold, the PBO number and the NPO number come
/// from the foundation rather than from this code — they are configuration,
/// because a registration number in a source file is a registration number
/// nobody can correct without a release.
/// </para>
/// <para>
/// <strong>Advisory.</strong> A mail server timeout must not undo a recorded
/// donation. The issued-at column is the record of whether a certificate has
/// gone out, so a donation that completes while mail is down is picked up by the
/// nightly job on its next run rather than lost — which is also why this handler
/// records the issue <em>after</em> a successful send and not before.
/// </para>
/// </remarks>
public sealed class IssueSection18ACertificate(
    IDonationRepository donations,
    IEmailSender emailSender,
    Section18AOptions options,
    IClock clock,
    ILogger<IssueSection18ACertificate> logger)
    : IDomainEventListener<DonationCompleted>, IAdvisoryListener
{
    public async Task HandleAsync(DonationCompleted domainEvent, CancellationToken cancellationToken)
    {
        if (!domainEvent.WantsSection18A || domainEvent.Amount < options.Threshold)
        {
            return;
        }

        // A certificate without the foundation's real registration numbers is
        // not a Section 18A certificate: SARS would reject the deduction, and
        // the donor would find out at tax time. Until the numbers are
        // configured, the site does not offer certificates either.
        if (!options.CanIssueCertificates)
        {
            Log.Section18ANotConfigured(logger, domainEvent.DonationId);

            return;
        }

        var issuedAt = clock.Now;
        var certificate = Reference(domainEvent.DonationId, issuedAt);

        var sent = await emailSender.SendAsync(
            new EmailMessage(
                ToAddress: domainEvent.DonorEmail,
                ToName: domainEvent.DonorName,
                Subject: $"Your Section 18A certificate — {certificate}",
                HtmlBody: RenderHtml(domainEvent, certificate, issuedAt),
                TextBody: RenderText(domainEvent, certificate, issuedAt)),
            cancellationToken);

        if (!sent)
        {
            // Left unissued on purpose. The nightly job retries anything
            // completed more than an hour ago whose certificate never went out,
            // so the donor gets their certificate late rather than never.
            Log.Section18ADeferred(logger, domainEvent.DonationId);

            return;
        }

        await donations.RecordSection18AIssuedAsync(domainEvent.DonationId, issuedAt, cancellationToken);

        Log.Section18AIssued(logger, certificate, domainEvent.DonationId);
    }

    /// <summary>
    /// A human-readable, unique certificate reference: <c>S18A-2026-0F3A91</c>.
    /// </summary>
    /// <remarks>
    /// Derived from the donation id rather than from a counter, so re-issuing the
    /// same certificate produces the same reference. A donor holding two copies
    /// of one certificate with two different numbers has, as far as SARS is
    /// concerned, a problem.
    /// </remarks>
    private static string Reference(Guid donationId, DateTimeOffset issuedAt)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(donationId.ToString()));

        return $"S18A-{issuedAt:yyyy}-{Convert.ToHexString(digest)[..6]}";
    }

    private string RenderHtml(DonationCompleted donation, string certificate, DateTimeOffset issuedAt)
    {
        var rows = new (string Label, string Value)[]
        {
            ("Certificate number", certificate),
            ("Date issued", issuedAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)),
            ("Donor", donation.DonorName),
            ("Amount received", Money(donation.Amount)),
            ("Nature of donation", "Cash"),
            ("PBO registration", options.PboNumber),
            ("NPO registration", options.NpoNumber),
        };

        var tableRows = new StringBuilder();

        foreach (var (label, value) in rows)
        {
            tableRows.Append(CultureInfo.InvariantCulture, $"""
                <tr>
                  <td style="padding:9px 0;font:400 12px/1.5 Arial,sans-serif;color:#8E887C;width:170px">{WebUtility.HtmlEncode(label)}</td>
                  <td style="padding:9px 0;font:600 13px/1.5 Arial,sans-serif;color:#17150F">{WebUtility.HtmlEncode(value)}</td>
                </tr>
                """);
        }

        // Table-based layout with inline styles, which is archaic everywhere
        // except email: Outlook and Gmail still strip <style> blocks and ignore
        // flexbox, so a certificate laid out the modern way arrives as a column
        // of unstyled text. The Heritage palette is carried through by hand.
        return $"""
            <!doctype html>
            <html lang="en"><body style="margin:0;background:#DEDACF;padding:24px 0">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                   style="max-width:600px;margin:0 auto;background:#FAF6EE">
              <tr><td style="background:#17150F;padding:22px 32px;text-align:center">
                <div style="font:600 18px/1 Georgia,serif;letter-spacing:.16em;color:#FAF6EE">THEE BUHLE</div>
                <div style="font:600 8px/1 Arial,sans-serif;letter-spacing:.3em;color:#9A9384;margin-top:5px">FOUNDATION</div>
              </td></tr>
              <tr><td style="padding:32px 32px 0">
                <p style="margin:0 0 6px;font:600 11px/1 Arial,sans-serif;letter-spacing:.2em;text-transform:uppercase;color:#A87C32">Tax certificate</p>
                <h1 style="margin:0 0 8px;font:600 26px/1.2 Georgia,serif;color:#17150F">Section 18A receipt</h1>
                <p style="margin:0 0 24px;font:400 14px/1.7 Arial,sans-serif;color:#5A554A">
                  Thank you, {WebUtility.HtmlEncode(donation.DonorName)}. This certificate is issued in terms of
                  section 18A of the Income Tax Act, 1962, and may be submitted to SARS in support of a deduction.
                </p>
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-top:1px solid #E4DCCB">
                  {tableRows}
                </table>
              </td></tr>
              <tr><td style="padding:24px 32px 32px">
                <div style="background:#F2E7D2;padding:16px 18px;font:400 12px/1.6 Arial,sans-serif;color:#6B4F1E">
                  Keep this certificate. We can re-send it at any time, but SARS requires the original
                  certificate number, which is {WebUtility.HtmlEncode(certificate)}.
                </div>
              </td></tr>
              <tr><td style="padding:20px 32px;border-top:1px solid #E4DCCB;font:400 11px/1.6 Arial,sans-serif;color:#8E887C">
                {WebUtility.HtmlEncode(options.PostalAddress)}<br>
                Registered {WebUtility.HtmlEncode(options.NpoDisplay)} · {WebUtility.HtmlEncode(options.PboDisplay)}
              </td></tr>
            </table>
            </body></html>
            """;
    }

    private string RenderText(DonationCompleted donation, string certificate, DateTimeOffset issuedAt) =>
        string.Join('\n', [
            "THEE BUHLE FOUNDATION",
            "Section 18A tax certificate",
            string.Empty,
            $"Certificate number: {certificate}",
            $"Date issued:        {issuedAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}",
            $"Donor:              {donation.DonorName}",
            $"Amount received:    {Money(donation.Amount)}",
            "Nature of donation: Cash",
            $"PBO registration:   {options.PboNumber}",
            $"NPO registration:   {options.NpoNumber}",
            string.Empty,
            "Issued in terms of section 18A of the Income Tax Act, 1962.",
            string.Empty,
            "Thee Buhle Foundation",
            options.PostalAddress,
        ]);

    /// <summary>Rand, with a thin space between thousands: R1 800.00</summary>
    private static string Money(decimal amount) =>
        "R" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(",", " ", StringComparison.Ordinal);
}

/// <summary>
/// The foundation's tax-certificate details.
/// </summary>
/// <remarks>
/// Configuration, not constants. These are registration numbers held by a real
/// organisation: they change, they get corrected, and a wrong one on a
/// certificate is a wrong one on a donor's tax return.
/// </remarks>
public sealed record Section18AOptions
{
    public required string PboNumber { get; init; }

    public required string NpoNumber { get; init; }

    /// <summary>
    /// Whether an NPO number is configured, so the site may state it.
    /// </summary>
    /// <remarks>
    /// The binder leaves a missing setting null despite <c>required</c>, which is
    /// why this checks for null and not only for blank.
    /// </remarks>
    public bool HasNpoNumber => !string.IsNullOrWhiteSpace(NpoNumber);

    /// <summary>Whether a PBO number is configured, so the site may state it.</summary>
    public bool HasPboNumber => !string.IsNullOrWhiteSpace(PboNumber);

    /// <summary>
    /// Whether the site may offer, promise or issue Section 18A certificates.
    /// </summary>
    /// <remarks>
    /// Needs both numbers: the PBO registration is what makes a donation
    /// deductible, and the certificate states both.
    /// </remarks>
    public bool CanIssueCertificates => HasNpoNumber && HasPboNumber;

    /// <summary>The NPO number labelled for display, e.g. <c>123-456 NPO</c>.</summary>
    /// <remarks>
    /// South African NPO numbers are conventionally written with the "NPO"
    /// suffix already, so the label is only added when it is missing.
    /// </remarks>
    public string NpoDisplay => Labelled(NpoNumber, "NPO");

    /// <summary>The PBO number labelled for display, e.g. <c>PBO 930012345</c>.</summary>
    public string PboDisplay => Labelled(PboNumber, "PBO");

    private static string Labelled(string? number, string label) =>
        number is null ? string.Empty
        : number.Contains(label, StringComparison.OrdinalIgnoreCase) ? number.Trim()
        : $"{label} {number.Trim()}";

    public string PostalAddress { get; init; } =
        "Thee Buhle Foundation · Cornwallis Street S.E.7, Vanderbijlpark, Gauteng, 1911";

    /// <summary>
    /// The smallest donation that gets a certificate.
    /// </summary>
    /// <remarks>
    /// Not a legal threshold — section 18A has none. It is an administrative one:
    /// a certificate is a numbered document the foundation has to account for,
    /// and issuing one for R20 costs more to track than the deduction is worth to
    /// the donor.
    /// </remarks>
    public decimal Threshold { get; init; } = 100m;
}

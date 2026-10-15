// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - PayFast (2026) PayFast Developer Documentation.
// =============================================================================

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Infrastructure.Payments;

/// <summary>
/// The PayFast handshake.
/// </summary>
/// <remarks>
/// <para>
/// PayFast because it is South African: it settles into a local bank account in
/// rand, it accepts the instant-EFT and SnapScan methods South African donors
/// actually use, and it does not charge a currency conversion on every gift the
/// way an international processor does. For a Vaal-based NPO taking R150
/// donations, a 3% conversion fee is a meaningful fraction of the gift.
/// </para>
/// <para>
/// <strong>No card data passes through this class, or through this system at
/// all.</strong> The browser posts the signed field set below to PayFast's own
/// domain, the donor enters their card there, and this application only ever
/// sees a reference and an amount. That is what keeps the whole platform out of
/// PCI-DSS scope — not a decision about how carefully to handle a card number,
/// but a decision never to receive one.
/// </para>
/// </remarks>
public sealed class PayFastGateway(
    PayFastOptions options,
    ILogger<PayFastGateway> logger) : IPaymentGateway
{
    /// <summary>
    /// PayFast's published notification source addresses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second line of defence, behind the signature. A correct signature
    /// from an address that is not PayFast means the shared passphrase has
    /// leaked — which is worth refusing and logging loudly rather than accepting
    /// on the strength of the signature alone.
    /// </para>
    /// <para>
    /// Held as hostnames rather than as literal IP addresses on purpose: PayFast
    /// changes the addresses behind these names, and a hard-coded IP list fails
    /// silently on the day they do — every donation stops being confirmed, with
    /// a correct signature, and nobody notices until the monthly reconciliation.
    /// </para>
    /// </remarks>
    private static readonly string[] NotifyHosts =
    [
        "www.payfast.co.za",
        "sandbox.payfast.co.za",
        "w1w.payfast.co.za",
        "w2w.payfast.co.za",
    ];

    public string GatewayName => "payfast";

    public string ProcessUrl => options.UseSandbox
        ? "https://sandbox.payfast.co.za/eng/process"
        : "https://www.payfast.co.za/eng/process";

    public IReadOnlyDictionary<string, string> BuildCheckoutFields(CheckoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (firstName, lastName) = SplitName(request.DisplayName);

        // Insertion order matters: PayFast's signature is computed over the
        // fields in the order they appear in the form, so a Dictionary<,> —
        // whose order is an implementation detail — would produce a signature
        // that sometimes validates and sometimes does not. An ordered
        // collection makes that impossible rather than unlikely.
        var fields = new List<KeyValuePair<string, string>>
        {
            new("merchant_id", options.MerchantId),
            new("merchant_key", options.MerchantKey),
            new("return_url", options.ReturnUrl),
            new("cancel_url", options.CancelUrl),
            new("notify_url", options.NotifyUrl),
            new("name_first", firstName),
            new("name_last", lastName),
            new("email_address", request.Email),

            // Our reference, not theirs. It is unique and it is what makes a
            // duplicated callback a no-op rather than a double count.
            new("m_payment_id", request.Reference),
            new("amount", request.Amount.ToString("F2", CultureInfo.InvariantCulture)),
            new("item_name", DescribeGift(request.Frequency)),
            new("item_description", "Donation to Thee Buhle Foundation"),
        };

        if (request.Frequency == DonationFrequency.Monthly)
        {
            // A monthly subscription rather than a repeated once-off, so the
            // donor's bank shows a recurring mandate they can see and cancel.
            fields.Add(new("subscription_type", "1"));
            fields.Add(new("billing_date", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            fields.Add(new("recurring_amount", request.Amount.ToString("F2", CultureInfo.InvariantCulture)));
            fields.Add(new("frequency", "3"));
            fields.Add(new("cycles", "0"));
        }

        var signature = Sign(fields);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in fields)
        {
            result[key] = value;
        }

        result["signature"] = signature;

        return result;
    }

    /// <summary>
    /// Whether a callback genuinely came from PayFast.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this check, the notify endpoint is a public URL that mints
    /// donations: anybody who can reach it posts <c>payment_status=COMPLETE</c>
    /// and the foundation's published total, and a Section 18A tax certificate,
    /// follow.
    /// </para>
    /// <para>
    /// The signature is recomputed over every field <em>except</em> the
    /// signature itself, in the order received, and compared in fixed time.
    /// </para>
    /// </remarks>
    public bool VerifyCallbackSignature(IReadOnlyDictionary<string, string> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.TryGetValue("signature", out var presented) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var fields = payload
            .Where(pair => !string.Equals(pair.Key, "signature", StringComparison.Ordinal))
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value))
            .ToList();

        var expected = Sign(fields);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// Whether the caller's address belongs to PayFast.
    /// </summary>
    /// <remarks>
    /// Resolved by DNS at call time rather than compared against a stored IP
    /// list, so PayFast can change their infrastructure without this silently
    /// rejecting every callback. A resolution failure is treated as untrusted —
    /// the safe direction, because the donation stays pending and the gateway
    /// retries.
    /// </remarks>
    public async Task<bool> IsTrustedSourceAsync(
        string sourceIp,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(sourceIp, out var caller))
        {
            return false;
        }

        if (options.UseSandbox && IPAddress.IsLoopback(caller))
        {
            // Local development only, and gated on the sandbox flag so it cannot
            // be reached in production even by accident.
            return true;
        }

        foreach (var host in NotifyHosts)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

                if (addresses.Contains(caller))
                {
                    return true;
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                // DNS is unavailable or the name does not resolve. Try the next
                // host rather than failing the whole check on one name.
            }
        }

        GatewayLog.UntrustedCallbackSource(logger, sourceIp);

        return false;
    }

    /// <summary>
    /// PayFast's signature: an MD5 over the URL-encoded field string plus the
    /// passphrase.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MD5 is PayFast's choice, not ours — it is what their API specifies, so
    /// there is no alternative that would still validate. It is worth being
    /// explicit that this is <strong>not</strong> a general endorsement: MD5 is
    /// broken for collision resistance and must never be used for a password or
    /// a content hash anywhere else in this system.
    /// </para>
    /// <para>
    /// It is tolerable here specifically because the property being relied on is
    /// <em>keyed</em> authentication over a short, structured message with a
    /// shared secret an attacker does not have — not collision resistance. A
    /// chosen-prefix collision does not help forge a payment notification for a
    /// different amount.
    /// </para>
    /// <para>
    /// The amount is checked against the stored donation independently, in
    /// <c>DonationService</c>, so the signature is not the only thing standing
    /// between a tampered amount and a receipt.
    /// </para>
    /// </remarks>
    private string Sign(IEnumerable<KeyValuePair<string, string>> fields)
    {
        var builder = new StringBuilder();

        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrEmpty(value))
            {
                // Empty fields are omitted, which is what PayFast's own
                // implementation does. Including them produces a signature that
                // never matches, for a reason that is very hard to see.
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            // PayFast expects '+' for a space and upper-case percent escapes.
            builder.Append(key).Append('=').Append(
                Uri.EscapeDataString(value.Trim()).Replace("%20", "+", StringComparison.Ordinal));
        }

        if (!string.IsNullOrEmpty(options.Passphrase))
        {
            builder.Append("&passphrase=").Append(
                Uri.EscapeDataString(options.Passphrase.Trim()).Replace("%20", "+", StringComparison.Ordinal));
        }

#pragma warning disable CA5351 // MD5 is what the PayFast API specifies; see the remarks above.
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
#pragma warning restore CA5351
    }

    private static string DescribeGift(DonationFrequency frequency) => frequency switch
    {
        DonationFrequency.Monthly => "Monthly gift — Thee Buhle Foundation",
        DonationFrequency.Sponsor => "Sponsor a learner — Thee Buhle Foundation",
        _ => "Donation — Thee Buhle Foundation",
    };

    /// <summary>
    /// Splits a display name into the two fields PayFast requires.
    /// </summary>
    /// <remarks>
    /// The form asks for one name, because asking a donor to split their own
    /// name into two boxes is friction on a donation page. A single-word name
    /// gets a placeholder surname rather than an empty one, since PayFast
    /// rejects a blank <c>name_last</c> — and refusing somebody's donation
    /// because they go by one name would be a poor reason to lose a gift.
    /// </remarks>
    private static (string First, string Last) SplitName(string displayName)
    {
        var parts = displayName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => ("Anonymous", "Donor"),
            1 => (parts[0], "-"),
            _ => (parts[0], parts[1]),
        };
    }
}

/// <summary>
/// The merchant credentials and the URLs PayFast calls back to.
/// </summary>
/// <remarks>
/// Every value here comes from configuration. The merchant key and the
/// passphrase are secrets: the passphrase is what makes a callback signature
/// unforgeable, so a leak of it is a leak of the ability to mint donations.
/// They live in Azure App Service application settings and in GitHub repository
/// secrets for CI, never in source and never in <c>appsettings.json</c>.
/// </remarks>
public sealed record PayFastOptions
{
    public required string MerchantId { get; init; }

    public required string MerchantKey { get; init; }

    /// <summary>The shared secret the callback signature is computed with.</summary>
    public required string Passphrase { get; init; }

    public required string ReturnUrl { get; init; }

    public required string CancelUrl { get; init; }

    /// <summary>
    /// Where PayFast posts the server-to-server confirmation.
    /// </summary>
    /// <remarks>
    /// Must be publicly reachable and must be https. This is the URL that
    /// actually moves a donation to complete — the donor's browser returning to
    /// the return URL proves nothing, because the donor controls their own
    /// browser.
    /// </remarks>
    public required string NotifyUrl { get; init; }

    public bool UseSandbox { get; init; }
}

internal static partial class GatewayLog
{
    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Warning,
        Message = "A payment callback arrived from an untrusted address ({SourceIp}) and was refused. "
            + "If its signature was valid, treat the PayFast passphrase as compromised.")]
    public static partial void UntrustedCallbackSource(ILogger logger, string sourceIp);
}

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Infrastructure.Security;

/// <summary>
/// Keyed signatures for two things: a consent record, and a time-limited
/// download link.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The consent signature.</strong> What the system records is not a
/// picture of a signature or a typed name — it is a keyed hash over the facts
/// that make the consent evidence: <em>this guardian, authenticated, made this
/// decision, at this time, from this address, on this form, for this learner</em>.
/// </para>
/// <para>
/// That is better evidence than an image, which could have been copied from
/// another form, and better than a typed name, which anybody could type. It is
/// verifiable after the fact — recompute it from the stored row and it either
/// matches or the row has been altered — and it is unforgeable without the
/// server key. Task 1 section 9.3.13.6.
/// </para>
/// <para>
/// <strong>The download signature.</strong> Learning resources are served from
/// storage rather than proxied through the API, so the URL has to carry its own
/// authorisation. A signed, expiring link does that without a session, and
/// without making the URL itself a permanent credential.
/// </para>
/// </remarks>
public sealed class HmacUrlSigner(SigningOptions options, IClock clock) : IUrlSigner
{
    /// <summary>
    /// A separate key from the JWT's.
    /// </summary>
    /// <remarks>
    /// Two keys rather than one, so that a leaked download key does not let
    /// anybody forge a consent record or a session. Key separation by purpose
    /// costs one configuration value.
    /// </remarks>
    private readonly byte[] key = Encoding.UTF8.GetBytes(options.SigningKey);

    public string SignConsent(
        Guid formId,
        Guid learnerId,
        Guid parentId,
        ConsentDecision decision,
        DateTimeOffset signedAt,
        string ipAddress)
    {
        // Every field is length-delimited by the separator and the separator
        // cannot appear in any field — all six are GUIDs, an enum name, an
        // ISO-8601 timestamp and an IP address. Without that property, two
        // different sets of facts could concatenate to the same string and
        // produce the same signature.
        var payload = string.Join('|',
            "consent.v1",
            formId.ToString("D", CultureInfo.InvariantCulture),
            learnerId.ToString("D", CultureInfo.InvariantCulture),
            parentId.ToString("D", CultureInfo.InvariantCulture),
            decision.ToString(),
            signedAt.ToString("O", CultureInfo.InvariantCulture),
            ipAddress);

        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
    }

    public string SignDownload(Guid resourceId, Guid userId, DateTimeOffset expiresAt)
    {
        var payload = BuildDownloadPayload(resourceId, userId, expiresAt.ToUnixTimeSeconds());

        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    /// Whether a download link is genuine and still valid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The expiry arrives as its own parameter — the controller reads it from a
    /// query string — but it is part of the <em>signed payload</em>, so editing
    /// <c>?expires=</c> to next year changes what the signature has to be and
    /// the check fails. An expiry outside the signature would be an expiry the
    /// client sets.
    /// </para>
    /// <para>
    /// Two details of the order here matter. The signature is verified before
    /// the expiry, because the other way round tells an attacker whether a
    /// forged signature happened to carry a live expiry. And the comparison is
    /// fixed-time, because a loop that returns on the first differing byte
    /// leaks how long a prefix matched, one request at a time.
    /// </para>
    /// </remarks>
    public bool VerifyDownload(Guid resourceId, Guid userId, DateTimeOffset expiresAt, string signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var payload = BuildDownloadPayload(resourceId, userId, expiresAt.ToUnixTimeSeconds());
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expected)))
        {
            return false;
        }

        return expiresAt > clock.Now;
    }

    private static string BuildDownloadPayload(Guid resourceId, Guid userId, long expires) =>
        string.Join('|',
            "download.v1",
            resourceId.ToString("D", CultureInfo.InvariantCulture),
            // The user is in the signature, so a link cannot be forwarded and
            // used by somebody else within its lifetime.
            userId.ToString("D", CultureInfo.InvariantCulture),
            expires.ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// The signing key for consents and download links.
/// </summary>
/// <remarks>
/// Deliberately not the JWT key. Rotating this one invalidates outstanding
/// download links, which is harmless; rotating it would also make previously
/// issued <em>consent</em> signatures unverifiable, which is not — so a rotation
/// has to keep the old key for verification. Recorded here because it is the one
/// operational trap in this class.
/// </remarks>
public sealed record SigningOptions
{
    public required string SigningKey { get; init; }

    /// <summary>How long a download link stays valid. Minutes, not days.</summary>
    public TimeSpan DownloadLifetime { get; init; } = TimeSpan.FromMinutes(15);
}

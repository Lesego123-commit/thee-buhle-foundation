// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Domain.Common;

namespace TheeBuhle.Application.Abstractions;

// =============================================================================
// Ports.
//
// The application layer's outward-facing interfaces. Everything here is
// something the services need *done* but must not know *how* — hashing a
// password, signing a token, sending an email, delivering a push.
//
// They are declared here rather than in Infrastructure so the dependency arrow
// keeps pointing inward: Infrastructure implements these, the services depend on
// them, and a unit test substitutes a two-line fake. That is the whole reason
// the business rules can be tested with no database, no SMTP server and no
// Azure account.
// =============================================================================

/// <summary>
/// Password hashing and verification.
/// </summary>
/// <remarks>
/// Argon2id, as committed to in Task 1 section 9.3.13.4. Behind an interface
/// because the cost parameters are configuration and because the unit tests
/// should not spend 60 MiB and four passes per assertion — the auth tests
/// substitute a fast fake and the real implementation has its own tests.
/// </remarks>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);

    /// <summary>
    /// Whether an existing hash was made with weaker parameters than the current
    /// configuration, so it can be upgraded on next sign-in rather than leaving
    /// old accounts on old costs forever.
    /// </summary>
    bool NeedsRehash(string hash);

    /// <summary>
    /// Verifies against a throwaway hash, to spend about the same time as a real
    /// verify would.
    /// </summary>
    /// <remarks>
    /// Called on the unknown-email path. Without it, an unknown address returns
    /// in microseconds and a known one takes as long as Argon2id does — so the
    /// <em>timing</em> enumerates accounts even though the message does not.
    /// </remarks>
    void DummyVerify(string password);
}

/// <summary>Issues and verifies the access token.</summary>
public interface ITokenService
{
    /// <summary>A short-lived signed access token for this principal.</summary>
    string IssueAccessToken(Guid userId, Role role, TimeSpan lifetime);

    /// <summary>
    /// A new opaque refresh token: the value to hand the client, and the hash to
    /// store. The value itself is never persisted.
    /// </summary>
    (string Token, string Hash) IssueRefreshToken();

    /// <summary>Hashes a presented refresh token so it can be looked up.</summary>
    string HashRefreshToken(string token);
}

/// <summary>
/// Allow-list HTML sanitisation for the two fields stored as markup:
/// announcement bodies and news post bodies.
/// </summary>
/// <remarks>
/// Applied on write, not on read. Sanitising on write is the stronger choice:
/// the database cannot contain a payload at all, so a future template that
/// forgets to escape, or a second client that renders the field differently, is
/// not a new vulnerability.
/// </remarks>
public interface IHtmlSanitiser
{
    string Sanitise(string dirtyHtml);

    /// <summary>
    /// A plain-text rendering, for a push notification or an SMS preview where
    /// markup is noise.
    /// </summary>
    string ToPlainText(string html);
}

/// <summary>Sends one email.</summary>
/// <remarks>
/// Submission goes through the domain's own authenticated mail server so SPF,
/// DKIM and DMARC all pass. Section 18A tax certificates and safeguarding
/// notices cannot be allowed to land in spam.
/// </remarks>
public interface IEmailSender
{
    bool IsEnabled { get; }

    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
    string ToAddress,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    string? ReplyTo = null);

/// <summary>
/// Strategy: one way of delivering one notification.
/// </summary>
/// <remarks>
/// <para>
/// The problem this solves is stated in Task 1 section 9.3.12.1. A parent with a
/// smartphone gets push; a parent on a feature phone gets SMS; a corporate donor
/// gets email. The <em>message</em> is the same and the <em>delivery
/// algorithm</em> varies, so the notifier picks an implementation at runtime from
/// the parent's stored preference rather than branching on channel type itself.
/// </para>
/// <para>
/// The payoff is that adding WhatsApp later means adding one class and one enum
/// member — no caller changes.
/// </para>
/// </remarks>
public interface INotificationChannel
{
    DeliveryChannel Channel { get; }

    /// <summary>
    /// Whether this channel can reach this user at all — a phone number on file,
    /// a live device token.
    /// </summary>
    Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> SendAsync(Guid userId, NotificationPayload payload, CancellationToken cancellationToken = default);
}

public sealed record NotificationPayload(
    Guid NotificationId,
    NotificationCategory Category,
    string Title,
    string Body,
    string? Icon = null,
    string? Link = null);

/// <summary>
/// Picks the Strategy at runtime.
/// </summary>
/// <remarks>
/// This is the piece that keeps the Strategy pattern honest: the notifier holds
/// a resolver, not a chain of <c>if</c> statements, and the choice is driven by
/// data in the preferences table rather than by code.
/// <para>
/// Resolution order: the category is off for this user → null, and nothing is
/// sent or stored; otherwise the user's preferred channel if it can actually
/// reach them; otherwise the in-app channel, which always can. That last step
/// falls back to something <em>cheaper and always available</em>, never to
/// something more expensive — a push failure must not silently start sending SMS
/// at the foundation's expense.
/// </para>
/// </remarks>
public interface INotificationChannelResolver
{
    Task<INotificationChannel?> ResolveAsync(Guid userId, NotificationCategory category, CancellationToken cancellationToken = default);
}

/// <summary>
/// Signs short-lived URLs: a resource download, and the anti-abuse token on the
/// public website's forms.
/// </summary>
public interface IUrlSigner
{
    /// <summary>
    /// A signature over the resource, the requesting user and an expiry, so the
    /// link cannot be edited to reach a different file and stops working shortly
    /// afterwards.
    /// </summary>
    string SignDownload(Guid resourceId, Guid userId, DateTimeOffset expiresAt);

    bool VerifyDownload(Guid resourceId, Guid userId, DateTimeOffset expiresAt, string signature);

    /// <summary>An HMAC over the facts a consent decision records.</summary>
    string SignConsent(Guid formId, Guid learnerId, Guid parentId, ConsentDecision decision, DateTimeOffset signedAt, string ipAddress);
}

/// <summary>
/// The payment gateway handshake.
/// </summary>
/// <remarks>
/// Behind an interface so the donation rules can be tested without a gateway,
/// and so the signature verification has its own tests. No implementation of
/// this ever sees a card number: the browser posts the signed field set to the
/// gateway's own domain.
/// </remarks>
public interface IPaymentGateway
{
    string GatewayName { get; }

    string ProcessUrl { get; }

    /// <summary>The signed hidden fields the browser posts to the gateway.</summary>
    IReadOnlyDictionary<string, string> BuildCheckoutFields(CheckoutRequest request);

    /// <summary>
    /// Whether a callback genuinely came from the gateway.
    /// </summary>
    /// <remarks>
    /// Re-signs the payload with the shared passphrase and compares with a
    /// fixed-time comparison. Without this, anybody who can reach the endpoint
    /// can post "payment complete" and mint a donation.
    /// </remarks>
    bool VerifyCallbackSignature(IReadOnlyDictionary<string, string> payload);

    /// <summary>
    /// Whether the caller's address is a known gateway host.
    /// </summary>
    /// <remarks>
    /// A correct signature from an unexpected address means the passphrase has
    /// leaked, which is worth refusing and logging loudly.
    /// </remarks>
    Task<bool> IsTrustedSourceAsync(string sourceIp, CancellationToken cancellationToken = default);
}

public sealed record CheckoutRequest(
    string Reference,
    string DisplayName,
    string Email,
    decimal Amount,
    DonationFrequency Frequency);

/// <summary>
/// The current request's caller, where a service needs it for auditing.
/// </summary>
/// <remarks>
/// Kept as a port so the services never touch <c>HttpContext</c>. A CLI job
/// supplies a system context instead, and a test supplies whatever it is
/// asserting about.
/// </remarks>
public interface IRequestContext
{
    string IpAddress { get; }

    string UserAgent { get; }
}

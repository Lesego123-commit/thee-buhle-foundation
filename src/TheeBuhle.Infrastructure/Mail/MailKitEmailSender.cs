// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using TheeBuhle.Application.Abstractions;

namespace TheeBuhle.Infrastructure.Mail;

/// <summary>
/// Sends mail through the foundation's own authenticated SMTP server.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not</strong> through SendGrid, Mailgun or an Azure relay, and the
/// reason is deliverability rather than cost. The domain's DNS — the zone file
/// the client sent, in <c>hosting details/</c> — already publishes SPF and DKIM
/// records that authorise <em>their</em> mail server. Submitting through that
/// server means SPF, DKIM and DMARC all pass without touching DNS.
/// </para>
/// <para>
/// Sending through a third party would mean either adding that provider to the
/// SPF record and setting up its DKIM keys, or having every message fail DMARC.
/// And the two messages that matter most here — a Section 18A tax certificate a
/// donor needs for SARS, and a safeguarding notice to the designated lead —
/// are exactly the ones that must not land in a spam folder.
/// </para>
/// <para>
/// The Afrihost mail server also stays where it is regardless of the API moving
/// to Azure: only the A record changes, the MX records do not. Using the
/// domain's own mail server is therefore the option that survives the migration
/// unchanged.
/// </para>
/// </remarks>
public sealed class MailKitEmailSender(
    SmtpOptions options,
    ILogger<MailKitEmailSender> logger) : IEmailSender
{
    /// <summary>
    /// Whether mail is configured at all.
    /// </summary>
    /// <remarks>
    /// False in development and in CI, where there is no mail server and no
    /// credential. Callers check this rather than discovering it through a
    /// timeout — and the email channel reports "cannot reach" instead of
    /// recording a delivery failure against every user.
    /// </remarks>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Host)
        && !string.IsNullOrWhiteSpace(options.Username);

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsEnabled)
        {
            // Logged at Information, not Warning. In development this is the
            // expected state, and a warning on every notification would train
            // people to ignore warnings.
            MailLog.MailNotConfigured(logger, message.Subject);

            return false;
        }

        var mime = new MimeMessage
        {
            Subject = message.Subject,
        };

        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            mime.ReplyTo.Add(new MailboxAddress(string.Empty, message.ReplyTo));
        }

        // Both parts, always. A plain-text alternative is what makes the message
        // readable in a text-only client, on a feature phone, and — in practice
        // more importantly — it lowers the spam score that a HTML-only
        // transactional email attracts.
        mime.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            client.Timeout = (int)options.Timeout.TotalMilliseconds;

            await client.ConnectAsync(
                options.Host,
                options.Port,
                // StartTls on 587, SslOnConnect on 465. Chosen from the port
                // rather than configured separately, because the two are not
                // independent — the wrong combination fails with a handshake
                // error that reads like a certificate problem.
                options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
                cancellationToken).ConfigureAwait(false);

            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken)
                .ConfigureAwait(false);

            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);

            MailLog.MailSent(logger, message.Subject);

            return true;
        }
        catch (Exception exception) when (exception
            is SmtpCommandException
            or SmtpProtocolException
            or AuthenticationException
            or System.Net.Sockets.SocketException
            or TimeoutException
            or IOException)
        {
            // False rather than a rethrow, because every caller has already
            // recorded the thing the email is about. A Section 18A certificate
            // that failed to send is retried by the nightly job; a notification
            // that failed to send is still in the recipient's in-app list; a
            // safeguarding alert that failed to send is still in the queue.
            //
            // The recipient address is deliberately not logged — it is personal
            // information, and the subject line is enough to identify which
            // message failed.
            MailLog.MailFailed(logger, message.Subject, exception.Message);

            return false;
        }
    }
}

/// <summary>
/// SMTP submission settings.
/// </summary>
/// <remarks>
/// The password is a secret and comes from configuration — an Azure App Service
/// application setting in production, user-secrets locally. It is a real mailbox
/// password for a real domain, so it is never in source and never in a committed
/// settings file.
/// </remarks>
public sealed record SmtpOptions
{
    public string Host { get; init; } = string.Empty;

    /// <summary>587 for submission with StartTLS, which is what the domain's server offers.</summary>
    public int Port { get; init; } = 587;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FromAddress { get; init; } = "noreply@theebuhlefoundation.co.za";

    public string FromName { get; init; } = "Thee Buhle Foundation";

    /// <summary>
    /// Deliberately short.
    /// </summary>
    /// <remarks>
    /// Mail is sent inside a request in a few places, so a hung SMTP connection
    /// would hold the request open. Fifteen seconds is long enough for a healthy
    /// server and short enough that a dead one degrades rather than hangs.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}

internal static partial class MailLog
{
    [LoggerMessage(
        EventId = 6201,
        Level = LogLevel.Debug,
        Message = "Email sent: {Subject}")]
    public static partial void MailSent(ILogger logger, string subject);

    [LoggerMessage(
        EventId = 6202,
        Level = LogLevel.Warning,
        Message = "Email could not be sent ({Subject}): {Reason}. "
            + "The underlying record was still saved.")]
    public static partial void MailFailed(ILogger logger, string subject, string reason);

    [LoggerMessage(
        EventId = 6203,
        Level = LogLevel.Information,
        Message = "Mail is not configured, so this message was not sent: {Subject}")]
    public static partial void MailNotConfigured(ILogger logger, string subject);
}

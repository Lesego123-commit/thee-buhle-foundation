// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Infrastructure.Notifications;

// =============================================================================
//  The Strategy pattern: four ways of delivering one notification.
//
//  Task 1 section 9.3.12.1. A parent with a smartphone gets push; a parent on a
//  feature phone gets SMS; a corporate donor gets email. The *message* is the
//  same and the *delivery algorithm* varies, so NotificationService holds a
//  resolver rather than a switch statement, and adding WhatsApp later is one
//  class and one enum member with no caller changes.
//
//  Every channel here returns false on failure rather than throwing. That is
//  the contract NotificationService depends on: the in-app row is written
//  before any channel is attempted, so a dead push token or an SMS provider
//  outage costs the parent a lock-screen alert and never costs them the
//  message itself.
// =============================================================================

/// <summary>
/// The in-app channel: the notification row, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The fallback of last resort, and the only channel that can always reach
/// anybody — it needs no phone number, no device token and no mail server,
/// because the row has already been written by the time a channel is asked to
/// deliver.
/// </para>
/// <para>
/// So <see cref="SendAsync"/> genuinely does nothing and returns true. That is
/// not a stub: the useful work happened when the notification was persisted, and
/// this channel exists so the resolver always has something to return.
/// </para>
/// </remarks>
public sealed class InAppChannel : INotificationChannel
{
    public DeliveryChannel Channel => DeliveryChannel.InApp;

    public Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> SendAsync(
        Guid userId,
        NotificationPayload payload,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

/// <summary>
/// The email channel.
/// </summary>
/// <remarks>
/// Renders the notification into a plain, readable message rather than a
/// newsletter. A notification that says "your child was marked absent" should
/// look like a note, not like marketing — partly because that is what it is, and
/// partly because a heavily-styled transactional email is more likely to be
/// filtered.
/// </remarks>
public sealed class EmailChannel(
    IEmailSender sender,
    IUserRepository users,
    ILogger<EmailChannel> logger) : INotificationChannel
{
    public DeliveryChannel Channel => DeliveryChannel.Email;

    public async Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!sender.IsEnabled)
        {
            return false;
        }

        return await users.FindContactAsync(userId, cancellationToken) is not null;
    }

    public async Task<bool> SendAsync(
        Guid userId,
        NotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var contact = await users.FindContactAsync(userId, cancellationToken);

        if (contact is null)
        {
            return false;
        }

        try
        {
            return await sender.SendAsync(
                new EmailMessage(
                    contact.Email,
                    contact.DisplayName,
                    payload.Title,
                    Render(payload, contact.DisplayName),
                    $"{payload.Title}\n\n{payload.Body}"),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // False, not a rethrow. The caller has already written the in-app
            // row, and a mail server that is down must not undo it.
            ChannelLog.ChannelFailed(logger, nameof(EmailChannel), exception.Message);

            return false;
        }
    }

    private static string Render(NotificationPayload payload, string name) =>
        $"""
        <!doctype html>
        <html lang="en"><body style="margin:0;background:#DEDACF;padding:24px 0">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
               style="max-width:560px;margin:0 auto;background:#FAF6EE">
          <tr><td style="background:#17150F;padding:18px 28px;text-align:center">
            <div style="font:600 16px/1 Georgia,serif;letter-spacing:.16em;color:#FAF6EE">THEE BUHLE</div>
          </td></tr>
          <tr><td style="padding:28px">
            <p style="margin:0 0 14px;font:400 14px/1.7 Arial,sans-serif;color:#5A554A">
              Hello {System.Net.WebUtility.HtmlEncode(name)},
            </p>
            <h1 style="margin:0 0 10px;font:600 20px/1.3 Georgia,serif;color:#17150F">
              {System.Net.WebUtility.HtmlEncode(payload.Title)}
            </h1>
            <p style="margin:0;font:400 14px/1.7 Arial,sans-serif;color:#5A554A">
              {System.Net.WebUtility.HtmlEncode(payload.Body)}
            </p>
          </td></tr>
          <tr><td style="padding:16px 28px;border-top:1px solid #E4DCCB;font:400 11px/1.6 Arial,sans-serif;color:#8E887C">
            You are receiving this because you have an account with Thee Buhle Foundation.
            You can change which notices you receive in the app, under Settings.
          </td></tr>
        </table>
        </body></html>
        """;
}

/// <summary>
/// The SMS channel.
/// </summary>
/// <remarks>
/// <para>
/// Not wired to a provider. Sending an SMS costs the foundation money per
/// message, and no account has been opened — so this channel reports that it
/// cannot reach anybody, and the resolver falls back to in-app.
/// </para>
/// <para>
/// That is the honest state of it and it is deliberately not disguised: a class
/// that pretended to send and returned true would make the delivery statistics
/// wrong, and a parent on a feature phone would appear to have been notified
/// when nothing left the building. When the client opens an account with a
/// provider, <see cref="CanReachAsync"/> and <see cref="SendAsync"/> are the
/// only two methods that change.
/// </para>
/// <para>
/// The stored phone number and preference are real, so a parent who has chosen
/// SMS has that choice recorded and honoured the moment the provider exists.
/// </para>
/// </remarks>
public sealed class SmsChannel(ILogger<SmsChannel> logger) : INotificationChannel
{
    public DeliveryChannel Channel => DeliveryChannel.Sms;

    public Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // No provider configured. Reported as "cannot reach" rather than as a
        // send failure, so the resolver falls back cleanly instead of recording
        // a failed delivery against every SMS-preferring parent.
        ChannelLog.ChannelNotConfigured(logger, nameof(SmsChannel));

        return Task.FromResult(false);
    }

    public Task<bool> SendAsync(
        Guid userId,
        NotificationPayload payload,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>
/// The push channel, over Expo's push service.
/// </summary>
/// <remarks>
/// <para>
/// Expo rather than Firebase Cloud Messaging directly. The mobile app is
/// Expo-managed, so Expo's service already holds the credentials for both APNs
/// and FCM — going direct would mean holding an Apple certificate and a Google
/// service account for a feature that sends a few hundred notifications a week.
/// </para>
/// <para>
/// A device token dies when the app is reinstalled or the OS rotates it, and the
/// only way to find out is to try. So a rejected token is deleted rather than
/// retried: leaving it means every future notification to that parent attempts a
/// send that cannot succeed.
/// </para>
/// </remarks>
public sealed class PushChannel(
    IHttpClientFactory httpClientFactory,
    INotificationRepository notifications,
    ILogger<PushChannel> logger) : INotificationChannel
{
    private const string ExpoPushEndpoint = "https://exp.host/--/api/v2/push/send";

    public DeliveryChannel Channel => DeliveryChannel.Push;

    public async Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await notifications.DeviceTokensForAsync(userId, cancellationToken)).Count > 0;

    public async Task<bool> SendAsync(
        Guid userId,
        NotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var tokens = await notifications.DeviceTokensForAsync(userId, cancellationToken);

        if (tokens.Count == 0)
        {
            return false;
        }

        var messages = tokens.Select(token => new
        {
            to = token,
            title = payload.Title,
            // The body is already short and already free of anything sensitive
            // — the safeguarding notification carries a pseudonym and a pointer,
            // never a note — because this text appears on a lock screen.
            body = payload.Body,
            data = new { link = payload.Link, notificationId = payload.NotificationId },
            sound = "default",
        }).ToList();

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(PushChannel));

            var response = await client
                .PostAsJsonAsync(ExpoPushEndpoint, messages, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                ChannelLog.ChannelFailed(
                    logger,
                    nameof(PushChannel),
                    $"HTTP {(int)response.StatusCode}");

                return false;
            }

            var receipt = await response.Content
                .ReadFromJsonAsync<ExpoPushResponse>(cancellationToken)
                .ConfigureAwait(false);

            return await ApplyReceiptAsync(receipt, tokens, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ChannelLog.ChannelFailed(logger, nameof(PushChannel), exception.Message);

            return false;
        }
    }

    /// <summary>
    /// Reads the per-message receipts and prunes the tokens the service says are
    /// dead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Expo answers 200 even when individual messages failed — the HTTP status
    /// describes the <em>request</em>, not the deliveries. So a channel that
    /// stopped at <c>IsSuccessStatusCode</c> would report success for a batch in
    /// which every token was rejected.
    /// </para>
    /// <para>
    /// <c>DeviceNotRegistered</c> is the one error worth acting on rather than
    /// logging. It means the app was uninstalled or the OS rotated the token, and
    /// the token will never work again — so it is deleted. Leaving it means every
    /// future notification to that person attempts a send that cannot succeed,
    /// forever, and records a failure each time.
    /// </para>
    /// <para>
    /// Other errors — a rate limit, a transient provider fault — are left alone,
    /// because those tokens are still good.
    /// </para>
    /// </remarks>
    private async Task<bool> ApplyReceiptAsync(
        ExpoPushResponse? receipt,
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        if (receipt?.Data is not { Count: > 0 } results)
        {
            // 200 with an unreadable body. Reported as a failure rather than
            // assumed to be a success, because the alternative is silently
            // claiming delivery the service never confirmed.
            ChannelLog.ChannelFailed(logger, nameof(PushChannel), "unreadable push receipt");

            return false;
        }

        var delivered = 0;

        // Zipped by position: Expo returns one receipt per message, in order.
        for (var i = 0; i < results.Count && i < tokens.Count; i++)
        {
            var result = results[i];

            if (string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                delivered++;

                continue;
            }

            if (string.Equals(result.Details?.Error, "DeviceNotRegistered", StringComparison.Ordinal))
            {
                await notifications.RemoveDeviceTokenAsync(tokens[i], cancellationToken).ConfigureAwait(false);

                ChannelLog.DeviceTokenPruned(logger);

                continue;
            }

            ChannelLog.ChannelFailed(
                logger,
                nameof(PushChannel),
                result.Details?.Error ?? result.Message ?? "unknown push error");
        }

        // True if it reached at least one of the person's devices. Somebody with
        // a phone and a tablet is notified even if the tablet's token is stale.
        return delivered > 0;
    }

    /// <summary>The shape of Expo's push response, cut down to what is used.</summary>
    private sealed record ExpoPushResponse(IReadOnlyList<ExpoPushResult>? Data);

    private sealed record ExpoPushResult(string? Status, string? Message, ExpoPushDetails? Details);

    private sealed record ExpoPushDetails(string? Error);
}

/// <summary>
/// Picks the channel at run time.
/// </summary>
/// <remarks>
/// <para>
/// This class is what keeps the Strategy pattern honest. The notifier holds a
/// resolver, not a chain of <c>if</c> statements, and the choice is driven by a
/// row in the preferences table rather than by code.
/// </para>
/// <para>The order, and why each step is where it is:</para>
/// <list type="number">
/// <item>
/// <strong>The category is switched off for this user → null.</strong> Nothing
/// is sent <em>and nothing is stored</em>. A preference that only suppressed the
/// push but still filled the in-app list would not be a preference.
/// </item>
/// <item>
/// <strong>Their preferred channel, if it can actually reach them.</strong>
/// Asking <c>CanReachAsync</c> first is what stops a parent who chose push, then
/// uninstalled the app, from silently receiving nothing.
/// </item>
/// <item>
/// <strong>Otherwise in-app</strong>, which always can.
/// </item>
/// </list>
/// <para>
/// The fallback goes to something <em>cheaper and always available</em>, never to
/// something more expensive. A push failure must not silently start sending SMS
/// at the foundation's expense — which is the obvious "helpful" behaviour and
/// would be a bill nobody authorised.
/// </para>
/// </remarks>
public sealed class ChannelResolver(
    IEnumerable<INotificationChannel> channels,
    INotificationRepository notifications) : INotificationChannelResolver
{
    private readonly Dictionary<DeliveryChannel, INotificationChannel> byChannel =
        channels.ToDictionary(channel => channel.Channel);

    public async Task<INotificationChannel?> ResolveAsync(
        Guid userId,
        NotificationCategory category,
        CancellationToken cancellationToken = default)
    {
        var preferred = await notifications.PreferredChannelAsync(userId, category, cancellationToken);

        // Null means the user has switched this category off. Returning null
        // propagates that all the way out: NotificationService stores nothing.
        if (preferred is null)
        {
            return null;
        }

        if (byChannel.TryGetValue(preferred.Value, out var channel)
            && await channel.CanReachAsync(userId, cancellationToken))
        {
            return channel;
        }

        // In-app is registered unconditionally, so this lookup cannot fail —
        // but TryGetValue rather than an indexer, because a misconfigured DI
        // registration should not throw inside a notification path that is
        // supposed to be incapable of failing.
        return byChannel.GetValueOrDefault(DeliveryChannel.InApp);
    }
}

internal static partial class ChannelLog
{
    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Warning,
        Message = "Delivery channel {ChannelName} failed: {Reason} — "
            + "the notification is still in the recipient's in-app list")]
    public static partial void ChannelFailed(ILogger logger, string channelName, string reason);

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Debug,
        Message = "Delivery channel {ChannelName} is not configured; falling back to in-app")]
    public static partial void ChannelNotConfigured(ILogger logger, string channelName);

    [LoggerMessage(
        EventId = 6103,
        Level = LogLevel.Information,
        Message = "A push token was rejected as no longer registered and has been removed")]
    public static partial void DeviceTokenPruned(ILogger logger);
}

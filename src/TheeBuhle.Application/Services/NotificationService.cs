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
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Queues a notification, then asks the resolver which Strategy should deliver it.
/// </summary>
/// <remarks>
/// <para>
/// The two steps are separate on purpose. The row is written first, so the
/// notice is in the app's list whatever happens next; only then is an external
/// channel attempted. A parent whose push token has expired still finds the
/// message waiting, which is not true of a design that treats push as the
/// delivery and the database as a log of it.
/// </para>
/// <para>
/// Nothing in this class throws. A notification is a consequence of something
/// else having succeeded, and a failed push must never undo a correctly captured
/// register — so a failure is recorded as
/// <see cref="DeliveryStatus.Failed"/> and the caller carries on. A failure
/// visible in the data is one somebody can act on.
/// </para>
/// </remarks>
public sealed class NotificationService(
    INotificationRepository notifications,
    INotificationChannelResolver channels,
    ILogger<NotificationService> logger)
{
    /// <summary>
    /// Queues and delivers one notification.
    /// </summary>
    /// <returns>
    /// The notification id, or null when the user has switched this category off
    /// — in which case nothing is stored and nothing is sent.
    /// </returns>
    public async Task<Guid?> NotifyAsync(
        Guid userId,
        NotificationCategory category,
        string title,
        string body,
        string? icon = null,
        string? link = null,
        CancellationToken cancellationToken = default)
    {
        var channel = await channels.ResolveAsync(userId, category, cancellationToken);

        if (channel is null)
        {
            // The user has turned this category off. Respecting that means
            // writing nothing at all — a suppressed notification that still
            // appears in the app is not suppressed.
            Log.NotificationSuppressed(logger, category, userId);

            return null;
        }

        var notification = new Notification
        {
            UserId = userId,
            Category = category,
            Icon = icon,
            Title = Truncate(title, 160),
            Body = Truncate(body, 400),
            Link = link,
            Channel = channel.Channel,
            DeliveryStatus = DeliveryStatus.Queued,
        };

        notifications.Queue(notification);

        var delivered = false;

        try
        {
            delivered = await channel.SendAsync(
                userId,
                new NotificationPayload(
                    notification.Id,
                    category,
                    notification.Title,
                    notification.Body,
                    icon,
                    link),
                cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An external provider threw. The row is already queued, so the
            // notice is not lost; it is recorded as failed and the caller is
            // never told, because the caller was doing something else.
            Log.DeliveryFailed(logger, channel.Channel, userId);
        }

        notification.DeliveryStatus = delivered ? DeliveryStatus.Sent : DeliveryStatus.Failed;

        if (!delivered)
        {
            Log.DeliveryFailed(logger, channel.Channel, userId);
        }

        return notification.Id;
    }

    /// <summary>
    /// Notifies several users with the same message.
    /// </summary>
    /// <returns>How many were actually queued, after preferences were respected.</returns>
    public async Task<int> NotifyManyAsync(
        IReadOnlyList<Guid> userIds,
        NotificationCategory category,
        string title,
        string body,
        string? icon = null,
        string? link = null,
        CancellationToken cancellationToken = default)
    {
        var sent = 0;

        foreach (var userId in userIds)
        {
            var id = await NotifyAsync(userId, category, title, body, icon, link, cancellationToken);

            if (id is not null)
            {
                sent++;
            }
        }

        return sent;
    }

    private static string Truncate(string value, int limit) =>
        value.Length <= limit ? value : value[..limit];
}

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>Notifications, delivery preferences and device tokens.</summary>
public sealed class NotificationRepository(TheeBuhleDbContext context, IClock clock) : INotificationRepository
{
    public void Queue(Notification notification) => context.Notifications.Add(notification);

    public async Task MarkDeliveredAsync(
        Guid notificationId,
        DeliveryStatus status,
        CancellationToken cancellationToken = default) =>
        await context.Notifications
            .Where(notification => notification.Id == notificationId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(notification => notification.DeliveryStatus, status)
                    .SetProperty(notification => notification.UpdatedAt, clock.Now),
                cancellationToken);

    public async Task<IReadOnlyList<Notification>> ForUserAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await context.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Marks one notification read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The user id is in the <c>WHERE</c> clause, not merely checked beforehand.
    /// That makes it structurally impossible to mark somebody else's
    /// notification read by guessing an id — the update simply affects no rows.
    /// </para>
    /// <para>
    /// It matters more than it looks: marking a notification read is how the app
    /// decides not to show it again, so an attacker who could do it to another
    /// account could suppress an absence notice or a permission-form deadline
    /// from a parent who would then never see it.
    /// </para>
    /// </remarks>
    public async Task<bool> MarkReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var affected = await context.Notifications
            .Where(notification => notification.Id == notificationId
                && notification.UserId == userId
                && notification.ReadAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(notification => notification.ReadAt, clock.Now)
                    .SetProperty(notification => notification.UpdatedAt, clock.Now),
                cancellationToken);

        return affected > 0;
    }

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Notifications
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(notification => notification.ReadAt, clock.Now)
                    .SetProperty(notification => notification.UpdatedAt, clock.Now),
                cancellationToken);

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Notifications
            .AsNoTracking()
            .CountAsync(
                notification => notification.UserId == userId && notification.ReadAt == null,
                cancellationToken);

    /// <summary>
    /// The channel this user wants for this category.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three outcomes, and the difference between the second and third is the
    /// interesting part:
    /// </para>
    /// <list type="bullet">
    /// <item>A row that is <em>disabled</em> → null. The user has switched this
    /// category off, so nothing is sent and nothing is stored.</item>
    /// <item>A row that is <em>enabled</em> → their chosen channel.</item>
    /// <item><em>No row at all</em> → push, the default. A user who has never
    /// opened the settings screen still gets notified; treating a missing
    /// preference as "off" would silently mute everybody who never configured
    /// anything.</item>
    /// </list>
    /// <para>
    /// So absence of a row is a default, and presence of a disabled row is a
    /// decision. Collapsing the two would either mute new users or override
    /// people who deliberately opted out.
    /// </para>
    /// </remarks>
    public async Task<DeliveryChannel?> PreferredChannelAsync(
        Guid userId,
        NotificationCategory category,
        CancellationToken cancellationToken = default)
    {
        var preference = await context.NotificationPreferences
            .AsNoTracking()
            .Where(row => row.UserId == userId && row.Category == category)
            .Select(row => new { row.Channel, row.Enabled })
            .FirstOrDefaultAsync(cancellationToken);

        if (preference is null)
        {
            return DeliveryChannel.Push;
        }

        return preference.Enabled ? preference.Channel : null;
    }

    public async Task<IReadOnlyList<NotificationPreference>> PreferencesForAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.NotificationPreferences
            .AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.Category)
            .ToListAsync(cancellationToken);

    public async Task SetPreferenceAsync(
        Guid userId,
        NotificationCategory category,
        DeliveryChannel channel,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.NotificationPreferences
            .FirstOrDefaultAsync(row => row.UserId == userId && row.Category == category, cancellationToken);

        if (existing is null)
        {
            await context.NotificationPreferences.AddAsync(
                new NotificationPreference
                {
                    UserId = userId,
                    Category = category,
                    Channel = channel,
                    Enabled = enabled,
                },
                cancellationToken);

            return;
        }

        existing.Channel = channel;
        existing.Enabled = enabled;
    }

    // ---------------------------------------------------------------- devices

    public async Task<IReadOnlyList<string>> DeviceTokensForAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.DeviceTokens
            .AsNoTracking()
            .Where(device => device.UserId == userId && device.RevokedAt == null)
            .Select(device => device.Token)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Registers a push token, or refreshes one already known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An upsert, because the app calls this on every launch — the OS can rotate
    /// a token at any time, so the app cannot know whether the one it holds is
    /// new. An insert would accumulate a row per launch and then send the same
    /// notification to the same phone a dozen times.
    /// </para>
    /// <para>
    /// A token found against a <em>different</em> user is reassigned rather than
    /// duplicated. That is the shared-phone case, which is common here: a parent
    /// and their child signing in on the same handset. The token belongs to the
    /// device, and the last person to sign in is the one whose notifications
    /// should arrive on it.
    /// </para>
    /// </remarks>
    public async Task RegisterDeviceTokenAsync(
        Guid userId,
        string token,
        DevicePlatform platform,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.DeviceTokens
            .FirstOrDefaultAsync(device => device.Token == token, cancellationToken);

        if (existing is null)
        {
            await context.DeviceTokens.AddAsync(
                new DeviceToken
                {
                    UserId = userId,
                    Token = token,
                    Platform = platform,
                    LastSeenAt = clock.Now,
                },
                cancellationToken);

            return;
        }

        existing.UserId = userId;
        existing.Platform = platform;
        existing.LastSeenAt = clock.Now;

        // Un-revokes a token that comes back. A reinstall can produce the same
        // token, and leaving it revoked would mean the person silently receives
        // nothing after reinstalling the app.
        existing.RevokedAt = null;
    }

    /// <summary>
    /// Deletes a token the push service rejected.
    /// </summary>
    /// <remarks>
    /// A real delete rather than a revocation flag, because a token the provider
    /// has declared unregistered has no evidential value — unlike a refresh
    /// token, where the revocation itself is worth keeping. Leaving it in place
    /// would mean attempting a delivery that cannot succeed on every future
    /// notification to that person, and recording a failure each time.
    /// </remarks>
    public async Task RemoveDeviceTokenAsync(string token, CancellationToken cancellationToken = default) =>
        await context.DeviceTokens
            .Where(device => device.Token == token)
            .ExecuteDeleteAsync(cancellationToken);
}

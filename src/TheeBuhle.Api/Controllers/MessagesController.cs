// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// The message inbox, notifications and delivery preferences.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is <strong>organisation to person</strong>. There is no
/// endpoint for one user to message another, and specifically none for a learner
/// to message a learner — which is a safeguarding decision rather than a missing
/// feature. Buddies get each other's first name and a group meeting time, and
/// the conversation happens in a hall with a staff member in it.
/// </para>
/// </remarks>
[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class MessagesController(
    ICommunicationRepository communications,
    INotificationRepository notifications,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>The caller's message inbox.</summary>
    /// <remarks>
    /// Unread first, then newest — so a parent opening the app sees what they
    /// have not dealt with rather than what happened most recently.
    /// </remarks>
    [HttpGet("messages")]
    [RequireUser]
    [ProducesResponseType(typeof(IReadOnlyList<InboxMessage>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InboxMessage>>> Inbox(
        [FromQuery] [Range(1, 100)] int limit = 30,
        CancellationToken cancellationToken = default) =>
        Ok(await communications.InboxForAsync(currentUser.Require().UserId, limit, cancellationToken));

    /// <summary>Marks a message read.</summary>
    /// <remarks>
    /// The caller's own id goes into the WHERE clause, so marking somebody
    /// else's message read affects no rows rather than being prevented by a check
    /// somebody has to remember. It matters: marking a message read is how the
    /// app decides not to show it again, so being able to do it to another
    /// account would suppress a permission-form deadline from a parent who would
    /// then never see it.
    /// </remarks>
    [HttpPost("messages/{announcementId:guid}/read")]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid announcementId, CancellationToken cancellationToken)
    {
        var marked = await communications.MarkAnnouncementReadAsync(
            announcementId,
            currentUser.Require().UserId,
            cancellationToken);

        // 404 rather than 403 when it is not theirs. A 403 would confirm that
        // the announcement exists, which is one id at a time but is still an
        // answer this endpoint has no reason to give.
        return marked ? NoContent() : NotFound();
    }

    /// <summary>The caller's notifications.</summary>
    [HttpGet("notifications")]
    [RequireUser]
    [ProducesResponseType(typeof(IReadOnlyList<Notification>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Notification>>> Notifications(
        [FromQuery] [Range(1, 100)] int limit = 30,
        CancellationToken cancellationToken = default) =>
        Ok(await notifications.ForUserAsync(currentUser.Require().UserId, limit, cancellationToken));

    /// <summary>How many unread notifications and messages the caller has.</summary>
    /// <remarks>
    /// Two counts from two tables, in one call. The app shows them as one badge
    /// but they are genuinely different things — a notification is a nudge, a
    /// message is a notice with a body — and collapsing them server-side would
    /// make the badge unexplainable when they disagree.
    /// </remarks>
    [HttpGet("notifications/unread-count")]
    [RequireUser]
    [ProducesResponseType(typeof(UnreadCounts), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCounts>> UnreadCount(CancellationToken cancellationToken)
    {
        var userId = currentUser.Require().UserId;

        return Ok(new UnreadCounts(
            await notifications.UnreadCountAsync(userId, cancellationToken),
            await communications.UnreadAnnouncementCountAsync(userId, cancellationToken)));
    }

    [HttpPost("notifications/{notificationId:guid}/read")]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkNotificationRead(
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var marked = await notifications.MarkReadAsync(
            notificationId,
            currentUser.Require().UserId,
            cancellationToken);

        return marked ? NoContent() : NotFound();
    }

    [HttpPost("notifications/read-all")]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var count = await notifications.MarkAllReadAsync(
            currentUser.Require().UserId,
            cancellationToken);

        return Ok(new { marked = count });
    }

    /// <summary>The caller's delivery preferences.</summary>
    /// <remarks>
    /// A category with no row is not "off" — it is the default, which is push.
    /// The absence of a row and the presence of a disabled one mean genuinely
    /// different things: treating a missing preference as "off" would silently
    /// mute everybody who has never opened the settings screen.
    /// </remarks>
    [HttpGet("notifications/preferences")]
    [RequireUser]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationPreference>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationPreference>>> Preferences(
        CancellationToken cancellationToken) =>
        Ok(await notifications.PreferencesForAsync(currentUser.Require().UserId, cancellationToken));

    /// <summary>Sets a delivery preference.</summary>
    /// <remarks>
    /// Switching a category off means <strong>nothing is sent and nothing is
    /// stored</strong> for it — not "delivered silently to the in-app list". A
    /// preference the system quietly works around is worse than no preference at
    /// all.
    /// </remarks>
    [HttpPut("notifications/preferences")]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPreference(
        [FromBody] SetPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await notifications.SetPreferenceAsync(
            currentUser.Require().UserId,
            request.Category,
            request.Channel,
            request.Enabled,
            cancellationToken);

        return NoContent();
    }

    /// <summary>Registers this device for push notifications.</summary>
    /// <remarks>
    /// <para>
    /// Called on every app launch, because the OS can rotate a token at any time
    /// and the app cannot know whether the one it holds is still current. An
    /// upsert, so re-registering does not accumulate a row per launch and then
    /// send the same notification to one phone a dozen times.
    /// </para>
    /// <para>
    /// A token already registered to a different user is reassigned rather than
    /// duplicated. That is the shared-phone case, which is common here — a parent
    /// and their child signing in on one handset.
    /// </para>
    /// </remarks>
    [HttpPost("notifications/device")]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await notifications.RegisterDeviceTokenAsync(
            currentUser.Require().UserId,
            request.Token,
            request.Platform,
            cancellationToken);

        return NoContent();
    }
}

public sealed record UnreadCounts(int Notifications, int Messages);

public sealed record SetPreferenceRequest
{
    [Required]
    public NotificationCategory Category { get; init; }

    [Required]
    public DeliveryChannel Channel { get; init; }

    public bool Enabled { get; init; } = true;
}

public sealed record RegisterDeviceRequest
{
    [Required]
    [MaxLength(255)]
    public required string Token { get; init; }

    [Required]
    public DevicePlatform Platform { get; init; }
}

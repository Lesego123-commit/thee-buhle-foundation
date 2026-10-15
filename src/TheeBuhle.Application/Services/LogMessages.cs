// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Source-generated log messages for the application layer.
/// </summary>
/// <remarks>
/// <para>
/// The <c>[LoggerMessage]</c> source generator writes the implementation of each
/// partial method at compile time. Three things come out of that which a plain
/// <c>logger.LogInformation($"...")</c> does not give:
/// </para>
/// <list type="bullet">
/// <item>No allocation when the level is disabled — the generated code checks
/// <c>IsEnabled</c> before touching the arguments, so a debug line costs nothing
/// in production.</item>
/// <item>The message template and its parameters are checked against each other
/// at compile time, so a template with three holes and two arguments is a build
/// error rather than a malformed log line discovered during an incident.</item>
/// <item>Event ids are stable, so an alert can be written against one.</item>
/// </list>
/// <para>
/// Collecting them in one file per layer also makes the log surface reviewable:
/// this is every line the application layer can emit, and it is short enough to
/// read and check that nothing sensitive is in a template.
/// </para>
/// <para>
/// Note what the templates do <em>not</em> contain: no learner name, no note
/// text, no donor email, no token. Learners appear by their pseudonymous
/// reference code. A log file is not a place for an identified child.
/// </para>
/// </remarks>
internal static partial class Log
{
    // ------------------------------------------------------------ attendance

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Register captured for session {SessionId}: {Recorded} new, {Amended} amended")]
    public static partial void RegisterCaptured(ILogger logger, Guid sessionId, int recorded, int amended);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Learner summary projected for {LearnerReference}: {AttendanceRate}%")]
    public static partial void SummaryProjected(ILogger logger, string learnerReference, int attendanceRate);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Awarded {Count} badge(s) to {LearnerReference}")]
    public static partial void BadgesAwarded(ILogger logger, int count, string learnerReference);

    // --------------------------------------------------------------- auth
    // Deliberately at Warning, because a 401 spike is a signal worth alerting on
    // (Task 1 section 9.3.13.8) and Information gets filtered in production.

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Sign-in succeeded for {UserId} as {Role}")]
    public static partial void SignInSucceeded(ILogger logger, Guid userId, Role role);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Sign-in failed: {Reason} from {IpAddress}")]
    public static partial void SignInFailed(ILogger logger, string reason, string ipAddress);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "Account {UserId} locked after {Attempts} failed attempts")]
    public static partial void AccountLocked(ILogger logger, Guid userId, int attempts);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Refresh token reuse detected for {UserId} — every session revoked")]
    public static partial void RefreshTokenReuse(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Warning,
        Message = "Password changed for {UserId}")]
    public static partial void PasswordChanged(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "{Purpose} code issued for {UserId} by {IssuedBy}")]
    public static partial void AccountCodeIssued(ILogger logger, AccountCodePurpose purpose, Guid userId, Guid issuedBy);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Information,
        Message = "{Purpose} code redeemed by {UserId}")]
    public static partial void AccountCodeRedeemed(ILogger logger, AccountCodePurpose purpose, Guid userId);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Warning,
        Message = "Account code refused ({Reason}) from {IpAddress}")]
    public static partial void AccountCodeRefused(ILogger logger, string reason, string ipAddress);

    [LoggerMessage(
        EventId = 2104,
        Level = LogLevel.Information,
        Message = "{Role} account {UserId} created by {CreatedBy}")]
    public static partial void AccountCreated(ILogger logger, Role role, Guid userId, Guid createdBy);

    // ------------------------------------------------------- safeguarding
    // Every line here is at Warning or above. A safeguarding event that only
    // appears at Information would be invisible in a production log.

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Safeguarding concern for {LearnerReference} escalated to {RecipientId} (urgent: {Urgent})")]
    public static partial void ConcernEscalated(ILogger logger, string learnerReference, Guid recipientId, bool urgent);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Error,
        Message = "Safeguarding concern for {LearnerReference} could not be routed — the check-in was NOT saved")]
    public static partial void ConcernUnroutable(ILogger logger, string learnerReference);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Warning,
        Message = "{Count} mentor clearance(s) expired and were marked as such")]
    public static partial void ClearancesExpired(ILogger logger, int count);

    // ------------------------------------------------------------- consent

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Consent {Decision} recorded for {LearnerReference} on form {FormId}")]
    public static partial void ConsentRecorded(ILogger logger, ConsentDecision decision, string learnerReference, Guid formId);

    // ----------------------------------------------------------- donations

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Donation {DonationId} completed: {Amount} {Frequency}")]
    public static partial void DonationCompleted(ILogger logger, Guid donationId, decimal amount, DonationFrequency frequency);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Warning,
        Message = "Gateway callback refused ({Reason}) for reference {Reference} from {IpAddress}")]
    public static partial void GatewayCallbackRefused(ILogger logger, string reason, string reference, string ipAddress);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Warning,
        Message = "Section 18A certificate for {DonationId} was not sent — it will be retried")]
    public static partial void Section18ADeferred(ILogger logger, Guid donationId);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Information,
        Message = "Section 18A certificate {Certificate} issued for donation {DonationId}")]
    public static partial void Section18AIssued(ILogger logger, string certificate, Guid donationId);

    [LoggerMessage(
        EventId = 5005,
        Level = LogLevel.Warning,
        Message = "Section 18A certificate for {DonationId} not issued: no PBO/NPO registration numbers are configured")]
    public static partial void Section18ANotConfigured(ILogger logger, Guid donationId);

    // ------------------------------------------------------ notifications

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Debug,
        Message = "Notification suppressed by preference: {Category} for {UserId}")]
    public static partial void NotificationSuppressed(ILogger logger, NotificationCategory category, Guid userId);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Warning,
        Message = "Delivery failed on {Channel} for {UserId} — the notification is still in their in-app list")]
    public static partial void DeliveryFailed(ILogger logger, DeliveryChannel channel, Guid userId);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Information,
        Message = "Announcement {AnnouncementId} fanned out to {Recipients} recipients")]
    public static partial void AnnouncementFannedOut(ILogger logger, Guid announcementId, int recipients);

    // ------------------------------------------------------------- events

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Debug,
        Message = "Domain event {EventName} dispatched to {Listeners} listener(s)")]
    public static partial void EventDispatched(ILogger logger, string eventName, int listeners);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Warning,
        Message = "Advisory listener {Listener} failed on {EventName}: {Reason}")]
    public static partial void AdvisoryListenerFailed(ILogger logger, string listener, string eventName, string reason);

    // -------------------------------------------------------------- impact

    [LoggerMessage(
        EventId = 8001,
        Level = LogLevel.Information,
        Message = "Impact metrics republished for {SchoolYear}: {Count} figures")]
    public static partial void ImpactRefreshed(ILogger logger, int schoolYear, int count);

    [LoggerMessage(
        EventId = 8002,
        Level = LogLevel.Information,
        Message = "Resource download authorised: {ResourceId} for {UserId}")]
    public static partial void DownloadAuthorised(ILogger logger, Guid resourceId, Guid userId);

    // -------------------------------------------------------- engagement

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Information,
        Message = "Volunteer application {ApplicationId} received")]
    public static partial void VolunteerApplicationReceived(ILogger logger, Guid applicationId);

    [LoggerMessage(
        EventId = 9002,
        Level = LogLevel.Warning,
        Message = "Child-safety concern reported through the website ({MessageId}) — routed to the safeguarding lead")]
    public static partial void SafeguardingEnquiryReceived(ILogger logger, Guid messageId);

    [LoggerMessage(
        EventId = 9003,
        Level = LogLevel.Information,
        Message = "Event registration {RegistrationId} created for {PartySize} attendee(s)")]
    public static partial void EventRegistrationCreated(ILogger logger, Guid registrationId, int partySize);

    // --------------------------------------------------------- projections

    [LoggerMessage(
        EventId = 10001,
        Level = LogLevel.Debug,
        Message = "Learner summary projected for {LearnerReference}: {AttendanceRate}%")]
    public static partial void LearnerSummaryProjected(ILogger logger, string learnerReference, int attendanceRate);

    // --------------------------------------------------------------- peers

    [LoggerMessage(
        EventId = 11001,
        Level = LogLevel.Information,
        Message = "Buddy pairing requested: {RequesterReference} → {RecipientReference} (awaiting staff approval)")]
    public static partial void PairingRequested(ILogger logger, string requesterReference, string recipientReference);
}

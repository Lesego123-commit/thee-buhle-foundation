// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Domain.Repositories;

// =============================================================================
// Repository interfaces.
//
// These are declared in the Domain layer and implemented in Infrastructure, and
// that inversion is what makes the layering in Task 1 section 9.3.12.2 real: the
// domain declares what it needs, Infrastructure supplies it, and the arrow
// points inward against the flow of calls. It is why the business rules can be
// compiled and tested with no database, no Azure account and no network.
//
// The interfaces are kept at the size of the *consumer's* need rather than the
// provider's surface — IReadOnlyLearningProgress below is two methods because
// the badge rules need two, not because that is all the learning repository can
// do.
// =============================================================================

/// <summary>
/// The Unit of Work.
/// </summary>
/// <remarks>
/// <para>
/// Capturing a register writes attendance rows, awards badges and queues
/// notifications. Those must commit together or not at all, so the services get
/// one commit boundary rather than each repository saving independently.
/// </para>
/// <para>
/// EF Core's <c>DbContext</c> already is a Unit of Work; this interface exists so
/// the application layer can depend on that fact without depending on EF Core.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction, committing on success
    /// and rolling back on any exception.
    /// </summary>
    /// <remarks>
    /// Nested calls join the outer transaction rather than opening a second one,
    /// so a service may call another service that also asks for a transaction
    /// without the inner commit releasing the outer one.
    /// </remarks>
    Task<T> TransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);

    Task TransactionAsync(Func<Task> work, CancellationToken cancellationToken = default);
}

/// <summary>
/// Credential and account-state storage.
/// </summary>
/// <remarks>
/// <see cref="FindCredentialsByEmailAsync"/> is the only method that returns a
/// password hash, it is called by exactly one service, and nothing that builds a
/// response goes near it.
/// </remarks>
public interface IUserRepository
{
    Task<User?> FindCredentialsByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the request principal, resolving the role-specific profile ids.
    /// Returns null for an account that is not active — which is how a
    /// suspension takes effect immediately rather than when the token expires.
    /// </summary>
    Task<AuthenticatedUser?> FindPrincipalAsync(Guid userId, CancellationToken cancellationToken = default);

    Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the failure counter and, at the threshold, sets the lock.
    /// </summary>
    /// <returns>The new failure count.</returns>
    /// <remarks>
    /// The increment and the lock decision happen in one statement, so two
    /// simultaneous guesses cannot both read "4 attempts" and each write "5",
    /// leaving the account unlocked after ten tries.
    /// </remarks>
    Task<int> RecordFailedLoginAsync(Guid userId, int lockThreshold, int lockMinutes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the hash and revokes every refresh token for the user. If the
    /// password was changed because it was compromised, an attacker holding a
    /// refresh token must not keep a live session.
    /// </summary>
    Task UpdatePasswordHashAsync(Guid userId, string hash, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);

    Task SetStatusAsync(Guid userId, UserStatus status, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>User ids in one audience, for announcement fan-out.</summary>
    Task<IReadOnlyList<Guid>> IdsForAudienceAsync(Audience audience, CancellationToken cancellationToken = default);

    Task<UserContact?> FindContactAsync(Guid userId, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------------
    //  The three role lookups the safeguarding escalation chain routes to.
    //
    //  Declared as three named methods rather than one
    //  FindStaffByJobTitle(string), on purpose. These are not a generic
    //  directory search — they are the three specific roles the organisation's
    //  child-protection policy names, and each returns null when nobody holds
    //  that role so the chain can pass the concern on.
    //
    //  A string-keyed lookup would let a typo silently produce "nobody holds
    //  this role", which for the safeguarding lead means a flagged concern is
    //  refused. Three methods make each role a compile-time fact.
    // ---------------------------------------------------------------------

    /// <summary>The mentor lead, or null if the role is vacant.</summary>
    Task<UserContact?> FindMentorLeadAsync(CancellationToken cancellationToken = default);

    /// <summary>The programme manager, or null if the role is vacant.</summary>
    Task<UserContact?> FindProgrammeManagerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The designated safeguarding lead, or null if there is none.
    /// </summary>
    /// <remarks>
    /// A filtered unique index guarantees there is at most one. Nothing
    /// guarantees there is at least one, so a null here is a configuration
    /// failure rather than a routing decision — and it is the one that stops a
    /// flagged check-in being saved at all.
    /// </remarks>
    Task<UserContact?> FindSafeguardingLeadAsync(CancellationToken cancellationToken = default);
}

/// <summary>A user's email and display name, for addressing a message to them.</summary>
public sealed record UserContact(Guid UserId, string Email, string DisplayName);

public interface ILearnerRepository
{
    Task<Learner?> FindAsync(Guid learnerId, CancellationToken cancellationToken = default);

    Task<Learner?> FindByReferenceAsync(string referenceCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// The authorisation primitive the whole parent-facing API rests on.
    /// </summary>
    /// <remarks>
    /// Resolves against <c>GuardianLink</c> and nothing else. There is no "or the
    /// learner has no other guardian", no "or the surname matches" — a parent
    /// sees a child because a staff member created a link row, full stop.
    /// </remarks>
    Task<bool> IsGuardianOfAsync(Guid parentId, Guid learnerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LearnerCard>> ForParentAsync(Guid parentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> LearnerIdsForMentorAsync(Guid mentorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The user ids of every active guardian linked to one learner — the answer
    /// to "who should hear about this child". Kept as its own method so that
    /// question has exactly one answer in the codebase.
    /// </summary>
    Task<IReadOnlyList<Guid>> GuardianUserIdsAsync(Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The register for one session: every actively enrolled learner, with any
    /// mark already captured joined on. One query, not one per learner.
    /// </summary>
    Task<IReadOnlyList<RegisterRow>> RegisterForSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The public Sponsor-a-Child cards.
    /// </summary>
    /// <remarks>
    /// The projection <em>is</em> the privacy decision: a reference code, a
    /// grade, a suburb and an ambition line — no name, no school, no photograph.
    /// Learners with an active sponsorship are excluded, so the page cannot be
    /// used to work out who is sponsored. Enforced in the query rather than by
    /// filtering afterwards, so a change to a controller cannot widen it.
    /// </remarks>
    Task<IReadOnlyList<SponsorCard>> SponsorableCardsAsync(int limit, CancellationToken cancellationToken = default);

    Task<Learner> CreateAsync(Learner learner, Guid actorId, CancellationToken cancellationToken = default);

    Task<PagedResult<LearnerCard>> SearchAsync(string? query, int? grade, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// The next unused code in the A-### series. Allocated inside the caller's
    /// transaction, with a unique index making a lost race an error rather than
    /// a duplicate.
    /// </summary>
    Task<string> NextReferenceCodeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the denormalised summary the mobile app shows on first paint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An upsert of a <em>recomputed</em> snapshot, not an increment. The
    /// difference matters: an increment that runs twice — a retried event, a
    /// replayed message — is wrong permanently and silently, whereas a recomputed
    /// snapshot that runs twice simply writes the same numbers again.
    /// </para>
    /// <para>
    /// Nothing treats this table as a source of truth, so a stale row costs the
    /// app one wrong number until the next Saturday and corrupts nothing.
    /// </para>
    /// </remarks>
    Task UpsertSummaryAsync(LearnerSummarySnapshot snapshot, CancellationToken cancellationToken = default);
}

public sealed record LearnerSummarySnapshot(
    Guid LearnerId,
    int SessionsRecorded,
    int SessionsAttended,
    int SessionsLate,
    int AttendanceRate,
    DateTimeOffset RefreshedAt);

public sealed record LearnerCard(
    Guid LearnerId,
    string ReferenceCode,
    string FirstName,
    string Surname,
    int Grade,
    string? School,
    string? Suburb,
    int AttendanceRate,
    bool IsPrimary);

public sealed record RegisterRow(
    Guid LearnerId,
    string ReferenceCode,
    string FirstName,
    string Surname,
    int Grade,
    Guid? AttendanceId,
    AttendanceStatus? Status,
    DateTimeOffset? ArrivedAt,
    string? Note);

public sealed record SponsorCard(
    string ReferenceCode,
    int Grade,
    string? Suburb,
    string? Ambition,
    int AttendanceRate);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int Pages => Total == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

    public bool HasMore => Page * PageSize < Total;
}

public interface IAttendanceRepository
{
    Task<AttendanceRecord?> FindForSessionAndLearnerAsync(Guid sessionId, Guid learnerId, CancellationToken cancellationToken = default);

    void Add(AttendanceRecord record);

    Task<IReadOnlyList<AttendanceEntryView>> RecentForLearnerAsync(Guid learnerId, int limit, CancellationToken cancellationToken = default);

    Task<AttendanceSummary> SummaryForLearnerAsync(Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consecutive attended sessions, counting back from the most recent.
    /// </summary>
    /// <remarks>
    /// Ordered by the session's own start time, not by when the row was written:
    /// a register captured late on Monday for Saturday's session must not break a
    /// streak. An excused absence breaks it too — the badge is for showing up.
    /// </remarks>
    Task<int> CurrentStreakAsync(Guid learnerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<AttendanceStatus, int>> StatusTotalsAsync(CancellationToken cancellationToken = default);
}

public sealed record AttendanceSummary(
    int SessionsRecorded,
    int SessionsAttended,
    int SessionsLate,
    int SessionsAbsent,
    int AttendanceRate)
{
    public static AttendanceSummary Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed record AttendanceEntryView(
    Guid AttendanceId,
    AttendanceStatus Status,
    DateTimeOffset? ArrivedAt,
    string? Note,
    Guid SessionId,
    DateTimeOffset StartsAt,
    string? SubjectFocus,
    string? Room,
    string? VenueName,
    string ProgrammeName);

public interface IBadgeRepository
{
    /// <summary>
    /// Active badge definitions of one criteria type. Criteria are rows, not
    /// code, so staff can add "Steady hand at six sessions" without a deployment.
    /// </summary>
    Task<IReadOnlyList<Badge>> ActiveByCriteriaAsync(BadgeCriteria criteria, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BadgeCatalogueEntry>> CatalogueForLearnerAsync(Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Awards a badge, relying on the composite primary key to make a repeat
    /// award impossible rather than checking first and racing.
    /// </summary>
    /// <returns>True if this call awarded it; false if it was already held.</returns>
    Task<bool> AwardAsync(Guid learnerId, Guid badgeId, CancellationToken cancellationToken = default);

    Task<bool> HoldsAsync(Guid learnerId, Guid badgeId, CancellationToken cancellationToken = default);

    Task<int> PointsForAsync(Guid learnerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AwardedBadge>> AwardedForAsync(Guid learnerId, CancellationToken cancellationToken = default);
}

public sealed record BadgeCatalogueEntry(
    Guid BadgeId,
    string Code,
    string Name,
    string Description,
    string? Icon,
    BadgeCriteria Criteria,
    int Threshold,
    int Points,
    bool Earned,
    DateTimeOffset? AwardedAt);

public sealed record AwardedBadge(
    Guid BadgeId,
    string Code,
    string Name,
    string Description,
    string? Icon,
    int Points,
    DateTimeOffset AwardedAt);

/// <summary>
/// The two learning measurements the badge rules need.
/// </summary>
/// <remarks>
/// A deliberately narrow interface. The badge evaluator does not need the whole
/// learning hub — it needs "how many resources has this learner finished" and
/// "what is their best mark improvement this year" — so that is all it is given,
/// and the badge tests satisfy it in a few lines instead of standing up the
/// whole resource repository. Interface segregation, at the size of the need.
/// </remarks>
public interface IReadOnlyLearningProgress
{
    Task<int> CompletedResourceCountAsync(Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>The largest term-on-term improvement across school-issued marks.</summary>
    Task<int> BestMarkImprovementAsync(Guid learnerId, int schoolYear, CancellationToken cancellationToken = default);
}

public interface IMentorshipRepository
{
    Task<MentorAssignment?> FindAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task<AssignmentView?> ActiveAssignmentForAsync(Guid learnerId, int schoolYear, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenteeView>> AssignmentsForMentorAsync(Guid mentorId, CancellationToken cancellationToken = default);

    Task<Mentor?> FindMentorAsync(Guid mentorId, CancellationToken cancellationToken = default);

    void AddAssignment(MentorAssignment assignment);

    void AddCheckIn(CheckIn checkIn);

    /// <summary>
    /// Check-ins for one learner.
    /// </summary>
    /// <param name="includeFlagged">
    /// Must be false for every caller except the safeguarding lead. The filter is
    /// applied in the query, not after the fetch, so a flagged note is never
    /// loaded into a process that is about to render a parent's screen.
    /// </param>
    Task<IReadOnlyList<CheckInView>> CheckInsForLearnerAsync(Guid learnerId, int limit, bool includeFlagged, CancellationToken cancellationToken = default);

    /// <summary>Open flagged concerns, for the safeguarding lead's queue only.</summary>
    Task<IReadOnlyList<ConcernView>> OpenConcernsAsync(CancellationToken cancellationToken = default);

    Task<int> CountCheckInsForAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a flagged concern reached a named person, and when.
    /// </summary>
    /// <remarks>
    /// Written inside the same transaction as the check-in, so a saved flagged
    /// check-in with no escalation row is a state the database cannot hold. That
    /// is the whole reason the escalation listener is not advisory.
    /// </remarks>
    Task MarkEscalatedAsync(Guid checkInId, Guid recipientUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a flagged concern has been dealt with, and how.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closing is an update on the existing row, never a delete: the concern is
    /// the safeguarding record and has to survive being resolved.
    /// </para>
    /// <para>
    /// This method exists because without it the safeguarding queue is
    /// append-only. Every concern ever flagged stays in it, the lead cannot tell
    /// what is outstanding from what was handled in March, and a capped list
    /// eventually pushes the oldest <em>unresolved</em> concern off the bottom —
    /// which is the one failure a safeguarding queue must not have.
    /// </para>
    /// </remarks>
    /// <returns>False if it was already closed, so a double submit is a no-op.</returns>
    Task<bool> CloseConcernAsync(Guid checkInId, Guid closedBy, string outcome, CancellationToken cancellationToken = default);
}

public sealed record AssignmentView(
    Guid AssignmentId,
    Guid MentorId,
    Guid LearnerId,
    int SchoolYear,
    string MentorName,
    string? Occupation);

public sealed record MenteeView(
    Guid AssignmentId,
    Guid LearnerId,
    string ReferenceCode,
    string FirstName,
    string Surname,
    int Grade,
    string? Suburb,
    int CheckInCount,
    DateOnly? LastCheckInOn);

public sealed record CheckInView(
    Guid CheckInId,
    DateOnly OccurredOn,
    CheckInMode Mode,
    WellbeingRating Wellbeing,
    string? Notes,
    IReadOnlyList<string> SupportAreas,
    bool ConcernFlagged,
    DateTimeOffset? EscalatedAt,
    string MentorName,
    string? Occupation);

public sealed record ConcernView(
    Guid CheckInId,
    DateOnly OccurredOn,
    WellbeingRating Wellbeing,
    IReadOnlyList<string> SupportAreas,
    string? Notes,
    Guid? EscalatedTo,
    DateTimeOffset? EscalatedAt,
    string ReferenceCode,
    int Grade,
    string? Suburb,
    string MentorName);

public interface IDonationRepository
{
    /// <summary>
    /// Finds a donor by email or creates one. Deduplicated on email so a monthly
    /// giver does not accumulate a donor row a month, which would break the
    /// Section 18A annual total.
    /// </summary>
    Task<Donor> FindOrCreateDonorAsync(string email, string displayName, string? phone, CancellationToken cancellationToken = default);

    void AddDonation(Donation donation);

    /// <summary>
    /// Finds a donation by our own gateway reference, <strong>with the donor
    /// loaded</strong>.
    /// </summary>
    /// <remarks>
    /// The donor is required rather than optional here: the callback raises
    /// <c>DonationCompleted</c>, which carries the donor's name and email for the
    /// receipt and the tax certificate. A lazy load at that point would be a
    /// second round trip inside a transaction, and a null donor would be a
    /// certificate addressed to nobody.
    /// </remarks>
    Task<Donation?> FindByGatewayReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<Donation?> FindAsync(Guid donationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a pending donation to complete.
    /// </summary>
    /// <returns>
    /// False if it was already complete — which is how duplicate webhook
    /// delivery becomes a no-op rather than a double count.
    /// </returns>
    Task<bool> MarkCompleteAsync(Guid donationId, DateTimeOffset receivedAt, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid donationId, string reason, CancellationToken cancellationToken = default);

    Task RecordSection18AIssuedAsync(Guid donationId, DateTimeOffset issuedAt, CancellationToken cancellationToken = default);

    /// <summary>Donations completed over an hour ago whose certificate never sent.</summary>
    Task<IReadOnlyList<Donation>> AwaitingSection18AAsync(DateTimeOffset before, int limit, CancellationToken cancellationToken = default);

    Task<PagedResult<DonationView>> RecentAsync(DonationStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Completed donations only — a pending redirect must never appear in a published total.</summary>
    Task<DonationTotals> TotalsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonthlyTotal>> MonthlyTotalsAsync(int months, CancellationToken cancellationToken = default);

    void AddSponsorship(Sponsorship sponsorship);

    Task<int> ActiveSponsorshipCountAsync(Guid learnerId, CancellationToken cancellationToken = default);
}

public sealed record DonationView(
    Guid DonationId,
    string DisplayName,
    string Email,
    bool IsOrganisation,
    decimal Amount,
    DonationFrequency Frequency,
    DonationStatus Status,
    string GatewayReference,
    bool WantsSection18A,
    DateTimeOffset? Section18AIssuedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset CreatedAt);

public sealed record DonationTotals(decimal Total, int Count, decimal MonthlyTotal, int MonthlyCount);

public sealed record MonthlyTotal(string Period, decimal Total);

public interface ICommunicationRepository
{
    void AddAnnouncement(Announcement announcement);

    Task<Announcement?> FindAnnouncementAsync(Guid announcementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the delivery rows for one announcement.
    /// </summary>
    /// <remarks>
    /// One bulk insert. On an all-parents announcement that is 200-odd rows, and
    /// 200 round trips is the difference between a publish that feels instant and
    /// one that times out.
    /// </remarks>
    Task<int> AddRecipientsAsync(Guid announcementId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>How many recipients an announcement was delivered to.</summary>
    Task<int> CountRecipientsAsync(Guid announcementId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InboxMessage>> InboxForAsync(Guid userId, int limit, CancellationToken cancellationToken = default);

    Task<bool> MarkAnnouncementReadAsync(Guid announcementId, Guid userId, CancellationToken cancellationToken = default);

    Task<int> UnreadAnnouncementCountAsync(Guid userId, CancellationToken cancellationToken = default);

    void AddPermissionForm(PermissionForm form);

    Task<PermissionForm?> FindPermissionFormAsync(Guid formId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permission forms still awaiting a decision for one learner.
    /// </summary>
    /// <remarks>
    /// Resolved through the latest consent by sequence, so a <em>withdrawn</em>
    /// consent correctly reappears as outstanding. Asking "has any consent row
    /// ever existed" instead would treat a withdrawal as an answer and hide the
    /// form — exactly the bug the append-only design exists to make visible.
    /// </remarks>
    Task<IReadOnlyList<OutstandingForm>> OutstandingFormsForAsync(Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a consent row.
    /// </summary>
    /// <remarks>
    /// There is deliberately no update or delete on this interface. The consent
    /// log is append-only, and the absence of the method is how that is enforced
    /// in the application layer.
    /// </remarks>
    void AppendConsent(Consent consent);

    /// <summary>The decision that stands right now, resolved by sequence.</summary>
    Task<Consent?> CurrentConsentAsync(Guid formId, Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>The full history, ordered by sequence rather than by timestamp.</summary>
    Task<IReadOnlyList<ConsentHistoryEntry>> ConsentHistoryAsync(Guid formId, Guid learnerId, CancellationToken cancellationToken = default);
}

public sealed record InboxMessage(
    Guid AnnouncementId,
    string Title,
    string BodyHtml,
    AnnouncementCategory Category,
    bool ActionRequired,
    DateOnly? DueDate,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ReadAt,
    string SenderName);

public sealed record OutstandingForm(
    Guid FormId,
    string Title,
    string Body,
    DateOnly DueDate,
    string? EventTitle,
    DateTimeOffset? EventStartsAt,
    string? EventLocation,
    string? ProgrammeName,
    ConsentDecision? LastDecision);

public sealed record ConsentHistoryEntry(
    Guid ConsentId,
    long Sequence,
    ConsentDecision Decision,
    DateTimeOffset SignedAt,
    string SignatureRef,
    string ParentName);

public interface INotificationRepository
{
    void Queue(Notification notification);

    Task MarkDeliveredAsync(Guid notificationId, DeliveryStatus status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ForUserAsync(Guid userId, int limit, CancellationToken cancellationToken = default);

    Task<bool> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The channel this user wants for this category.
    /// </summary>
    /// <returns>
    /// Null when the user has switched the category off entirely. Returning null
    /// is how a preference <em>suppresses</em> delivery rather than downgrading
    /// it — nothing is stored and nothing is sent.
    /// </returns>
    Task<DeliveryChannel?> PreferredChannelAsync(Guid userId, NotificationCategory category, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreference>> PreferencesForAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetPreferenceAsync(Guid userId, NotificationCategory category, DeliveryChannel channel, bool enabled, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------- devices

    /// <summary>The live push tokens for one user. Empty means push cannot reach them.</summary>
    Task<IReadOnlyList<string>> DeviceTokensForAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a device token, or refreshes the timestamp on one already known.
    /// </summary>
    /// <remarks>
    /// The app calls this on every launch, because a token can be rotated by the
    /// OS at any time. An upsert rather than an insert, so a re-registration does
    /// not accumulate a row per launch.
    /// </remarks>
    Task RegisterDeviceTokenAsync(Guid userId, string token, DevicePlatform platform, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a token the push service rejected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A device token dies when the app is reinstalled or the OS rotates it, and
    /// the only way to find out is for a send to be rejected. Deleting it then is
    /// what stops the system attempting a delivery that cannot succeed on every
    /// future notification to that person, forever.
    /// </para>
    /// <para>
    /// It also matters for the delivery statistics: a dead token left in place
    /// records a failed delivery every week and makes the notification success
    /// rate look like an outage.
    /// </para>
    /// </remarks>
    Task RemoveDeviceTokenAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>
/// The audit trail.
/// </summary>
/// <remarks>
/// A write method and reads, and no update or delete. Combined with the database
/// triggers, that is two independent reasons the trail cannot be rewritten —
/// which is the point of an audit trail.
/// </remarks>
public interface IAuditRepository
{
    void Write(
        Guid? userId,
        string action,
        string entityName,
        string? entityId,
        object? before,
        object? after,
        string ipAddress,
        string userAgent);

    Task<PagedResult<AuditView>> RecentAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditView>> ForEntityAsync(string entityName, string entityId, int limit, CancellationToken cancellationToken = default);
}

public sealed record AuditView(
    Guid Id,
    string Action,
    string EntityName,
    string? EntityId,
    string? ActorEmail,
    string? IpAddress,
    DateTimeOffset OccurredAt);

/// <summary>
/// Buddy pairing, behind an interface because there is a safeguarding rule over
/// it the unit tests need to exercise without a database.
/// </summary>
/// <remarks>
/// Note what the interface does <em>not</em> offer: no method creates an approved
/// pairing directly. The two-step create-then-decide shape is the rule,
/// expressed in the type.
/// </remarks>
public interface IBuddyPairingRepository
{
    Task<BuddyRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    void CreateRequest(BuddyRequest request);

    /// <summary>
    /// Records a decision on a pending request.
    /// </summary>
    /// <returns>False if the request had already been decided.</returns>
    Task<bool> DecideRequestAsync(Guid requestId, BuddyRequestStatus decision, Guid approvedBy, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BuddyRequestView>> RequestsForAsync(Guid learnerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingPairing>> PendingApprovalsAsync(CancellationToken cancellationToken = default);
}

public sealed record BuddyRequestView(
    Guid RequestId,
    BuddyRequestStatus Status,
    string Direction,
    string ReferenceCode,
    string FirstName,
    string SurnameInitial,
    int Grade,
    string? Suburb,
    DateTimeOffset CreatedAt);

public sealed record PendingPairing(
    Guid RequestId,
    DateTimeOffset CreatedAt,
    string RequesterReference,
    int RequesterGrade,
    string RecipientReference,
    int RecipientGrade);

/// <summary>
/// Creating people and their accounts, and the one-time codes that let them in.
/// </summary>
public interface IPeopleRepository
{
    Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>The next free learner reference, e.g. <c>A-264</c>.</summary>
    Task<string> NextLearnerReferenceAsync(CancellationToken cancellationToken = default);

    Task<bool> ProgrammeExistsAsync(Guid programmeId, CancellationToken cancellationToken = default);

    /// <summary>Which of these learners exist. Used to refuse a link to a learner that does not.</summary>
    Task<IReadOnlyList<Guid>> ExistingLearnersAsync(IReadOnlyCollection<Guid> learnerIds, CancellationToken cancellationToken = default);

    Task<bool> SafeguardingLeadExistsAsync(CancellationToken cancellationToken = default);

    void AddUser(User user);

    void AddLearner(Learner learner, Enrolment? enrolment);

    void AddParent(ParentGuardian parent, IEnumerable<GuardianLink> links);

    void AddMentor(Mentor mentor);

    void AddStaff(StaffMember staff);

    /// <summary>The account with its role and status, tracked for update.</summary>
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<User?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this account belongs to the designated safeguarding lead, who
    /// is a staff-side account even though their stored role is Staff.
    /// </summary>
    Task<bool> IsSafeguardingLeadAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The person's display name, from whichever profile the account has.</summary>
    Task<string> DisplayNameAsync(Guid userId, CancellationToken cancellationToken = default);

    void AddCode(AccountCode code);

    /// <summary>Marks every unused code for the account as revoked, so only the newest works.</summary>
    Task RevokeOpenCodesAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>The account's most recent code that is not yet used or revoked, tracked for update.</summary>
    Task<AccountCode?> LatestUnusedCodeAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PagedResult<PersonView>> SearchAsync(
        Role? role,
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>One account, as the staff console's People list shows it.</summary>
public sealed record PersonView(
    Guid UserId,
    string Name,
    string Email,
    Role Role,
    UserStatus Status,
    string? Detail,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? CodeExpiresAt);

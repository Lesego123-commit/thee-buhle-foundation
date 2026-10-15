// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;

namespace TheeBuhle.Domain.Repositories;

// ---------------------------------------------------------------------------
//  Read-side repositories
// ---------------------------------------------------------------------------
//
//  The repositories in Repositories.cs exist because a service has rules that
//  need testing without a database. The ones in this file mostly exist for a
//  different reason: they are projections of published rows onto the exact shape
//  one page needs, and they are behind an interface so the Application layer
//  keeps its inward-only dependency (Task 1 section 9.3.12.2) rather than
//  because their behaviour is interesting.
//
//  Two of them are interesting, though, and both are marked where it matters:
//
//   * IEngagementRepository.RemainingPlacesForUpdateAsync takes a row lock, so
//     two people registering the last place cannot both succeed.
//   * ILearningRepository's methods take the learner id as a *parameter* rather
//     than reading an ambient user, so progress can never leak between learners
//     by accident.
//
// ---------------------------------------------------------------------------

/// <summary>
/// Read models for the public website: programmes, events, news, impact.
/// </summary>
public interface IContentRepository
{
    Task<IReadOnlyList<ProgrammeView>> ProgrammesAsync(CancellationToken cancellationToken = default);

    Task<ProgrammeView?> ProgrammeAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VenueView>> VenuesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upcoming published events with a live registration count.
    /// </summary>
    /// <remarks>
    /// The count is computed, which is why the "58 places left" line on the
    /// events page can be trusted. A typed-in number goes stale the moment
    /// somebody registers.
    /// </remarks>
    Task<IReadOnlyList<EventView>> UpcomingEventsAsync(int limit, EventType? type = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventView>> PastEventsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Finds a published event by id or slug. Unpublished events are invisible.</summary>
    Task<EventView?> EventAsync(string idOrSlug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Published news posts.
    /// </summary>
    /// <remarks>
    /// "Published" means <c>PublishedAt</c> is set <em>and</em> is not in the
    /// future, so scheduling a post for next Monday genuinely hides it until
    /// then rather than relying on nobody guessing the URL.
    /// </remarks>
    Task<PagedResult<NewsSummary>> NewsAsync(NewsCategory? category, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<NewsArticle?> PostAsync(string slug, CancellationToken cancellationToken = default);

    Task<NewsSummary?> FeaturedPostAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The headline impact figures for one school year.
    /// </summary>
    /// <remarks>
    /// Read from the stored metrics that <c>ImpactService</c> refreshes, never
    /// aggregated live. Two reasons: the homepage cannot be slowed down by a
    /// table scan, and it cannot show a half-computed figure mid-refresh.
    /// </remarks>
    Task<IReadOnlyList<ImpactHeadline>> ImpactHeadlinesAsync(int schoolYear, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProgrammeImpact>> ProgrammeImpactAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubjectImprovement>> SubjectImprovementAsync(CancellationToken cancellationToken = default);
}

public sealed record ProgrammeView(
    Guid ProgrammeId,
    string Slug,
    string Number,
    string Name,
    string Summary,
    string Description,
    IReadOnlyList<string> Expectations,
    string? HowToJoin,
    string? CtaLabel,
    string? Audience,
    string? CapacityNote,
    int MinGrade,
    int MaxGrade);

public sealed record VenueView(Guid VenueId, string Name, string Address, int Capacity);

public sealed record EventView(
    Guid EventId,
    string Title,
    string Slug,
    EventType Type,
    string? VenueName,
    string? Location,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    decimal Fee,
    string? FeeNote,
    int Capacity,
    string? Description,
    int Registered)
{
    /// <summary>
    /// Places left, or null when the event is uncapped.
    /// </summary>
    /// <remarks>
    /// A capacity of zero means "no limit" in this schema, so this is null
    /// rather than a misleading negative number — and the front end renders
    /// nothing at all instead of "-12 places left".
    /// </remarks>
    public int? PlacesLeft => Capacity == 0 ? null : Math.Max(0, Capacity - Registered);

    public bool IsFull => Capacity != 0 && Registered >= Capacity;
}

public sealed record NewsSummary(
    Guid PostId,
    string Slug,
    NewsCategory Category,
    string Title,
    string Excerpt,
    string AuthorName,
    int ReadMinutes,
    bool IsFeatured,
    DateTimeOffset PublishedAt);

public sealed record NewsArticle(
    Guid PostId,
    string Slug,
    NewsCategory Category,
    string Title,
    string Excerpt,
    string BodyHtml,
    string AuthorName,
    int ReadMinutes,
    DateTimeOffset PublishedAt);

public sealed record ImpactHeadline(
    string MetricKey,
    string Label,
    string ValueText,
    string? Footnote,
    DateTimeOffset ComputedAt);

public sealed record ProgrammeImpact(
    Guid ProgrammeId,
    string Name,
    int Learners,
    int SessionsHeld,
    int AverageAttendance);

public sealed record SubjectImprovement(
    Guid SubjectId,
    string SubjectName,
    int LearnerCount,
    decimal AverageImprovement);

// ---------------------------------------------------------------------------

/// <summary>
/// Public engagement: event registrations, volunteer applications, contact
/// messages and the newsletter list.
/// </summary>
public interface IEngagementRepository
{
    /// <summary>
    /// Remaining places, <strong>with the event row locked</strong> for the rest
    /// of the transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the method that stops a camp with 120 places taking 121 bookings.
    /// Two requests for the last place read the same remaining count if capacity
    /// is checked outside a transaction, and both succeed — a race that only
    /// shows up under exactly the load that matters, on the morning registration
    /// opens.
    /// </para>
    /// <para>
    /// The lock means the second request waits for the first to commit and then
    /// reads a true count. Callers must already be inside a transaction; outside
    /// one the lock is released immediately and the guarantee is gone.
    /// </para>
    /// </remarks>
    /// <returns>Null when the event does not exist; int.MaxValue when uncapped.</returns>
    Task<int?> RemainingPlacesForUpdateAsync(Guid eventId, CancellationToken cancellationToken = default);

    void AddRegistration(EventRegistration registration);

    Task<bool> IsAlreadyRegisteredAsync(Guid eventId, string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegistrationView>> RegistrationsForEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    void AddVolunteerApplication(VolunteerApplication application);

    Task<VolunteerApplication?> FindVolunteerApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<PagedResult<VolunteerView>> VolunteerApplicationsAsync(VolunteerStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an application to a new status.
    /// </summary>
    /// <returns>False if it was already in that status, so a double-click is a no-op.</returns>
    Task<bool> SetVolunteerStatusAsync(Guid applicationId, VolunteerStatus status, Guid reviewerId, CancellationToken cancellationToken = default);

    void AddContactMessage(ContactMessage message);

    Task<PagedResult<ContactView>> ContactMessagesAsync(ContactStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<bool> SetContactStatusAsync(Guid messageId, ContactStatus status, Guid handlerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an address to the newsletter list, or revives a previously
    /// unsubscribed one.
    /// </summary>
    /// <remarks>
    /// Returns true only when this call actually changed something. Subscribing
    /// an address that is already on the list must not send a second welcome
    /// email, and must not reveal to the caller that the address was already
    /// there — which is why the API answers the same way either way.
    /// </remarks>
    Task<bool> SubscribeToNewsletterAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> UnsubscribeFromNewsletterAsync(string email, CancellationToken cancellationToken = default);
}

public sealed record RegistrationView(
    Guid RegistrationId,
    string AttendeeName,
    string Email,
    string? Phone,
    int PartySize,
    bool Paid,
    DateTimeOffset CreatedAt);

/// <summary>
/// A volunteer application as the vetting queue shows it.
/// </summary>
/// <remarks>
/// Note what is <em>not</em> here: the ID number. Only the last four digits are
/// stored at all (POPIA data minimisation, Task 1 section 9.3.13.6), and even
/// those are not on this projection, because a list screen has no use for them.
/// The full record carries them for the one staff member doing the police
/// clearance check.
/// </remarks>
public sealed record VolunteerView(
    Guid ApplicationId,
    string FullName,
    string Email,
    string Phone,
    string? Suburb,
    IReadOnlyList<string> Skills,
    VolunteerStatus Status,
    int DocumentCount,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt);

public sealed record ContactView(
    Guid MessageId,
    ContactTopic Topic,
    string Name,
    string? Organisation,
    string Email,
    string? Phone,
    string Body,
    ContactStatus Status,
    DateTimeOffset CreatedAt);

// ---------------------------------------------------------------------------

/// <summary>
/// The learning hub: subjects, resources and one learner's progress through them.
/// </summary>
/// <remarks>
/// Every method that returns progress takes the learner id as an argument. There
/// is no "current learner" anywhere in this interface, which is what makes it
/// structurally impossible for one learner's progress to appear on another's
/// screen because a caller forgot to pass a filter.
/// </remarks>
public interface ILearningRepository : IReadOnlyLearningProgress
{
    Task<IReadOnlyList<SubjectCard>> SubjectsForGradeAsync(int grade, Guid? learnerId, CancellationToken cancellationToken = default);

    Task<Subject?> FindSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourceCard>> ResourcesForSubjectAsync(Guid subjectId, int grade, Guid? learnerId, ResourceType? type, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourceCard>> SearchResourcesAsync(string query, int? grade, Guid? learnerId, int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourceCard>> RecentlyAddedAsync(int grade, int limit, CancellationToken cancellationToken = default);

    /// <summary>The one part-finished resource to offer as "continue where you left off".</summary>
    Task<ResourceCard?> ContinueWatchingAsync(Guid learnerId, CancellationToken cancellationToken = default);

    Task<LearningResource?> FindResourceAsync(Guid resourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records how far through a resource a learner has got.
    /// </summary>
    /// <remarks>
    /// Monotonic: a learner who scrubs back to the start of a video they have
    /// finished has not un-finished it, so progress never decreases. Without
    /// that, closing a video at the beginning would silently revoke a badge.
    /// </remarks>
    Task RecordProgressAsync(Guid learnerId, Guid resourceId, int percentComplete, CancellationToken cancellationToken = default);

    /// <summary>
    /// First and latest school-issued mark per subject for one school year.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "First" and "latest" mean <strong>by term</strong>, not by value. The
    /// implementation must order by the term and take the ends of that ordering;
    /// it must not use <c>MIN(mark)</c> and <c>MAX(mark)</c>.
    /// </para>
    /// <para>
    /// Spelling that out because the min/max version looks equivalent and is not.
    /// A learner who scores 62, then 71, then 58 has a latest mark of 58 and has
    /// gone backwards by four. Min/max reports a latest mark of 71 and an
    /// improvement of positive thirteen — and because it can only ever subtract
    /// the smallest from the largest, it reports a non-negative improvement for
    /// <em>every</em> learner, including one whose marks collapsed.
    /// </para>
    /// <para>
    /// That is not a cosmetic error. This figure feeds
    /// <see cref="IReadOnlyLearningProgress.BestMarkImprovementAsync"/>, which
    /// awards the "most improved" badge, and it feeds the subject-improvement
    /// figure published on the public impact page. Getting it wrong means a child
    /// who is struggling is told they are improving, and the foundation tells
    /// funders something untrue.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SubjectMarkTrend>> MarkTrendAsync(Guid learnerId, int schoolYear, CancellationToken cancellationToken = default);

    /// <summary>Every school-issued mark for one learner and year, oldest term first.</summary>
    Task<IReadOnlyList<MarkPoint>> MarkHistoryAsync(Guid learnerId, int schoolYear, CancellationToken cancellationToken = default);
}

public sealed record SubjectMarkTrend(
    Guid SubjectId,
    string SubjectName,
    decimal FirstMark,
    decimal LatestMark,
    int Entries)
{
    /// <summary>
    /// How far the learner has moved this year. Negative when marks have fallen,
    /// which is the whole point of computing it by term.
    /// </summary>
    public decimal Improvement => Math.Round(LatestMark - FirstMark, 1);
}

public sealed record SubjectCard(
    Guid SubjectId,
    string Name,
    int Grade,
    string? Icon,
    string? Accent,
    int ResourceCount,
    int CompletedCount);

public sealed record ResourceCard(
    Guid ResourceId,
    Guid SubjectId,
    string SubjectName,
    string Title,
    string? Topic,
    ResourceType Type,
    int Grade,
    long? SizeBytes,
    int? DurationMinutes,
    int PercentComplete,
    DateTimeOffset? PublishedAt);

public sealed record MarkPoint(string Term, string SubjectName, decimal Mark, MarkSource Source);

// ---------------------------------------------------------------------------

/// <summary>
/// Peer support reads: buddy suggestions and study groups.
/// </summary>
/// <remarks>
/// <para>
/// The buddy <em>request</em> operations are not here — they live on
/// <see cref="IBuddyPairingRepository"/>, which encodes the safeguarding rule in
/// its shape: no method on it creates an approved pairing directly, so
/// request-then-approve is expressed in the type rather than in a comment. This
/// interface holds only the reads that feed the screen.
/// </para>
/// <para>
/// Pairing is <strong>request then staff approval</strong>, never automatic.
/// That is a safeguarding decision, not a UX one: this system connects children
/// to each other, so an adult signs off every connection before it exists.
/// </para>
/// <para>
/// Note also that nothing in this interface returns a way to send a message.
/// There is no learner-to-learner messaging endpoint anywhere in the system —
/// buddies get each other's first name and a group meeting time, and the
/// conversation happens in a hall with a staff member in it.
/// </para>
/// </remarks>
public interface IPeerRepository
{
    /// <summary>
    /// Learners in the same grade who might make good study buddies.
    /// </summary>
    /// <remarks>
    /// Returns a first name and a reference code — no surnames, no contact
    /// details, no suburb. Enough to recognise a classmate, not enough to find a
    /// stranger's house.
    /// </remarks>
    Task<IReadOnlyList<BuddySuggestion>> SuggestedForAsync(Guid learnerId, int grade, int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudyGroupCard>> GroupsForAsync(Guid learnerId, int grade, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests to join a study group. Pending, never joined outright.
    /// </summary>
    /// <remarks>
    /// The same request-then-approve shape as buddy pairing, for the same
    /// safeguarding reason: an adult signs off before a child is added to a group
    /// of other children.
    /// </remarks>
    Task RequestGroupMembershipAsync(Guid groupId, Guid learnerId, CancellationToken cancellationToken = default);

    Task SetGroupMembershipStatusAsync(Guid groupId, Guid learnerId, MembershipStatus status, CancellationToken cancellationToken = default);
}

public sealed record BuddySuggestion(
    Guid LearnerId,
    string ReferenceCode,
    string FirstName,
    int Grade,
    IReadOnlyList<string> SharedSubjects);

public sealed record StudyGroupCard(
    Guid GroupId,
    string Name,
    string? SubjectName,
    string? MeetsAt,
    string? Location,
    int MemberCount,
    MembershipStatus? MyStatus);

// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------

/// <summary>
/// Sessions, and the enrolment check the register depends on.
/// </summary>
/// <remarks>
/// <para>
/// Lives here with every other repository interface rather than beside its
/// callers. It started out next to <c>AttendanceService</c> because it is small
/// and only two services use it, which was a reasonable-sounding reason and a
/// bad one: a second interface of the same name was later declared for the
/// dashboard's needs, and the two sat in different namespaces describing the
/// same table.
/// </para>
/// <para>
/// One home for repository contracts means that cannot happen again.
/// </para>
/// </remarks>
public interface ISessionRepository
{
    Task<TutoringSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The "next session" card on the parent dashboard, scoped to the programmes
    /// the learner is actively enrolled in — so a parent is never shown a session
    /// their child cannot attend.
    /// </summary>
    Task<SessionCard?> NextForLearnerAsync(Guid learnerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionCard>> UpcomingAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent sessions with how complete each register is. The admin console's
    /// landing view: a Saturday with 190 of 214 learners marked is the thing a
    /// programme manager needs to see first.
    /// </summary>
    Task<IReadOnlyList<RegisterState>> RecentWithRegisterStateAsync(int limit, CancellationToken cancellationToken = default);

    void Add(TutoringSession session);

    Task<bool> IsEnrolledInSessionProgrammeAsync(Guid learnerId, Guid sessionId, CancellationToken cancellationToken = default);
}

public sealed record SessionCard(
    Guid SessionId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? SubjectFocus,
    string? Room,
    string ProgrammeName,
    string VenueName,
    string VenueAddress);

public sealed record RegisterState(
    Guid SessionId,
    DateTimeOffset StartsAt,
    string? SubjectFocus,
    string? Room,
    string ProgrammeName,
    string VenueName,
    int Marked,
    int Expected);

// ---------------------------------------------------------------------------

/// <summary>
/// The aggregates behind the published impact figures, and the upsert that
/// republishes them.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IContentRepository"/> on purpose. That interface
/// <em>reads</em> the published metrics for the website; this one recomputes
/// them, which is a nightly job with a completely different cost profile — five
/// aggregate queries across the attendance table. Keeping them apart means a
/// page can never accidentally take the expensive path.
/// </para>
/// </remarks>
public interface IImpactRepository
{
    /// <summary>Distinct learners enrolled in any term of this school year.</summary>
    Task<int> LearnersSupportedAsync(int schoolYear, CancellationToken cancellationToken = default);

    Task<int> ActiveMentorCountAsync(int schoolYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tutoring hours delivered.
    /// </summary>
    /// <remarks>
    /// Attended sessions multiplied by each session's own length, so a
    /// three-hour Saturday and a two-hour workshop are not counted as the same
    /// thing. Counting sessions instead of hours would overstate the figure by
    /// roughly a third, and this is a number the foundation publishes to funders.
    /// </remarks>
    Task<int> TutoringHoursAsync(int schoolYear, CancellationToken cancellationToken = default);

    /// <summary>The percentage of this year's mentor assignments that ran to the end of the year.</summary>
    Task<int> MentorRetentionAsync(int schoolYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates one published metric.
    /// </summary>
    /// <remarks>
    /// Upsert rather than delete-then-insert. A refresh that deleted first would
    /// leave the impact page briefly empty, and a refresh that failed halfway
    /// would leave it empty for good.
    /// </remarks>
    Task UpsertMetricAsync(ImpactMetric metric, CancellationToken cancellationToken = default);
}

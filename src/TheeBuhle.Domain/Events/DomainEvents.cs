// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Domain.Common;

namespace TheeBuhle.Domain.Events;

// =============================================================================
// Domain events.
//
// Records rather than classes: an event is a statement of fact, and a fact
// should be immutable and comparable by value.
//
// Every Context dictionary below is written for a log file, which means it
// carries the learner's pseudonymous reference code rather than their name, and
// never the text of a safeguarding note or a donor's email address.
// =============================================================================

/// <summary>
/// Raised once per learner when a Saturday register is captured.
/// </summary>
/// <remarks>
/// <para>
/// This is the event that makes the Observer pattern worth having. Recording
/// attendance has three unrelated consequences — award a badge if a streak
/// completes, notify the parent, refresh the learner's cached summary — and
/// <c>AttendanceService</c> performs none of them. It raises this, and stays
/// closed to modification when a fourth consequence is added.
/// </para>
/// <para>
/// Task 1 section 9.3.12.1, Figure 3, is the object diagram of exactly this
/// moment.
/// </para>
/// </remarks>
public sealed record AttendanceRecorded(
    Guid AttendanceId,
    Guid SessionId,
    Guid LearnerId,
    string LearnerReference,
    AttendanceStatus Status,
    DateTimeOffset? ArrivedAt,
    Guid RecordedBy) : IDomainEvent
{
    public string Name => "attendance.recorded";

    /// <summary>Whether this mark counts as the learner having turned up.</summary>
    public bool WasPresent => Status.CountsAsAttended();

    public IReadOnlyDictionary<string, object?> Context => new Dictionary<string, object?>
    {
        ["attendance_id"] = AttendanceId,
        ["session_id"] = SessionId,
        // The pseudonym, not the learner's name — a log is not a place for an
        // identified child.
        ["learner_reference"] = LearnerReference,
        ["status"] = Status.ToString(),
    };
}

/// <summary>
/// Raised when a mentor flags a concern about a learner.
/// </summary>
/// <remarks>
/// The handler runs the Chain of Responsibility from Task 1 section 9.3.12.1:
/// mentor lead → programme manager → safeguarding lead, where the terminal
/// handler always escalates rather than dropping. None of that routing lives in
/// the mentor endpoint.
/// </remarks>
public sealed record ConcernFlagged(
    Guid CheckInId,
    Guid AssignmentId,
    Guid LearnerId,
    string LearnerReference,
    Guid MentorId,
    WellbeingRating Wellbeing,
    IReadOnlyList<string> SupportAreas) : IDomainEvent
{
    public string Name => "safeguarding.concern_flagged";

    /// <summary>
    /// Whether this concern skips the first two links of the chain.
    /// </summary>
    /// <remarks>
    /// A "concerned" rating alone is enough. So is any of the urgent areas, even
    /// at a calmer rating — a mentor who notes something about a child's home
    /// while rating them "steady" has still noted something about their home.
    /// </remarks>
    public bool IsUrgent =>
        Wellbeing == WellbeingRating.Concerned
        // The shared vocabulary, not a local copy. A local list is how "abuse"
        // came to be urgent on this event and unrecognised by the service that
        // raises it — so the tag was dropped before IsUrgent ever saw it.
        || SupportAreas.AnyUrgent();

    public IReadOnlyDictionary<string, object?> Context => new Dictionary<string, object?>
    {
        ["checkin_id"] = CheckInId,
        ["learner_reference"] = LearnerReference,
        ["wellbeing"] = Wellbeing.ToString(),
        // The note text is deliberately absent. A flagged note may contain
        // details about a child that must reach the safeguarding lead and
        // nothing else, least of all a log file.
        ["support_areas"] = string.Join(',', SupportAreas),
        ["urgent"] = IsUrgent,
    };
}

/// <summary>
/// Raised when a guardian records a decision on a permission form.
/// </summary>
/// <remarks>
/// Fires for a withdrawal too, because a withdrawal is a new decision rather
/// than the removal of an old one.
/// </remarks>
public sealed record ConsentSigned(
    Guid ConsentId,
    Guid FormId,
    Guid LearnerId,
    string LearnerReference,
    Guid ParentId,
    ConsentDecision Decision) : IDomainEvent
{
    public string Name => "consent.signed";

    public IReadOnlyDictionary<string, object?> Context => new Dictionary<string, object?>
    {
        ["consent_id"] = ConsentId,
        ["form_id"] = FormId,
        ["learner_reference"] = LearnerReference,
        ["decision"] = Decision.ToString(),
    };
}

/// <summary>
/// Raised when the payment gateway confirms a donation — not when the donor
/// presses "Continue to secure payment".
/// </summary>
/// <remarks>
/// The distinction is the whole point. The front end creates a pending donation
/// and hands the donor to the gateway; only the verified webhook moves it to
/// complete and raises this. So nothing on the public Impact page, and no
/// Section 18A certificate, can be triggered by a donor who abandoned checkout.
/// </remarks>
public sealed record DonationCompleted(
    Guid DonationId,
    Guid DonorId,
    string DonorEmail,
    string DonorName,
    decimal Amount,
    DonationFrequency Frequency,
    bool WantsSection18A) : IDomainEvent
{
    public string Name => "donation.completed";

    public IReadOnlyDictionary<string, object?> Context => new Dictionary<string, object?>
    {
        ["donation_id"] = DonationId,
        ["amount"] = Amount,
        ["frequency"] = Frequency.ToString(),
        // The donor's name and email are personal information and stay out of
        // the log. They are on the event because the certificate handler needs
        // them, not because they should be recorded.
    };
}

/// <summary>
/// Raised when staff publish an announcement or a permission-form notice.
/// </summary>
/// <remarks>
/// The handler writes the delivery rows for the audience and queues one
/// notification per recipient through whichever channel each has chosen.
/// Publishing is therefore one relational write plus an event; the delivery
/// mechanics are somebody else's problem.
/// </remarks>
public sealed record AnnouncementPublished(
    Guid AnnouncementId,
    string Title,
    string Summary,
    AnnouncementCategory Category,
    Audience Audience,
    bool ActionRequired,
    DateOnly? DueDate,
    Guid PublishedBy) : IDomainEvent
{
    public string Name => "announcement.published";

    public IReadOnlyDictionary<string, object?> Context => new Dictionary<string, object?>
    {
        ["announcement_id"] = AnnouncementId,
        ["category"] = Category.ToString(),
        ["audience"] = Audience.ToString(),
        ["action_required"] = ActionRequired,
    };
}

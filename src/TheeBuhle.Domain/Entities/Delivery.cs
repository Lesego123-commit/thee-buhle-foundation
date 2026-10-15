// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Domain.Common;

namespace TheeBuhle.Domain.Entities;

// =============================================================================
// ERD view 2 of 4 — programme delivery and mentorship
// Task 1 documentation, Figure 2b.
// =============================================================================

/// <summary>
/// One of the four programmes: tutoring, mentorship, life-skills workshops,
/// holiday camps.
/// </summary>
/// <remarks>
/// The public website's programme copy lives here rather than in a template, so
/// staff can change what the site says about a programme from the console
/// without a deployment.
/// </remarks>
public class Programme : Entity
{
    public required string Slug { get; set; }

    /// <summary>The "01"–"04" numeral the design uses as an ordinal.</summary>
    public required string Number { get; set; }

    public required string Name { get; set; }

    public required string Summary { get; set; }

    public required string Description { get; set; }

    /// <summary>
    /// "What participants can expect" — a list of strings.
    /// </summary>
    /// <remarks>
    /// Stored as JSON because the shape varies per programme and is never
    /// queried relationally. Task 1 section 9.3.11.4 makes the same call for
    /// volunteer skills and availability.
    /// </remarks>
    public List<string> Expectations { get; set; } = [];

    public string? HowToJoin { get; set; }

    /// <summary>The call-to-action wording, e.g. "Apply to be a mentor".</summary>
    public string? CtaLabel { get; set; }

    /// <summary>"Grades 8–12", "Ages 13–19".</summary>
    public string? Audience { get; set; }

    /// <summary>"34 open places", "12 learners waiting for a match".</summary>
    public string? CapacityNote { get; set; }

    public int MinGrade { get; set; } = 1;

    public int MaxGrade { get; set; } = 12;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<Enrolment> Enrolments { get; set; } = [];
    public ICollection<TutoringSession> Sessions { get; set; } = [];
}

/// <summary>Where sessions happen: the Sebokeng centre, the offices, a hall.</summary>
public class Venue : Entity
{
    public required string Name { get; set; }

    public required string Address { get; set; }

    public int Capacity { get; set; }

    public ICollection<TutoringSession> Sessions { get; set; } = [];
}

/// <summary>
/// A learner's place on a programme for one term.
/// </summary>
/// <remarks>
/// A unique index on (learner, programme, term) prevents duplicate enrolment.
/// Attendance can only be captured for a learner with an <em>active</em>
/// enrolment in the session's programme — marking somebody present at a session
/// they are not enrolled in would corrupt both their own attendance rate and the
/// programme figures the public Impact page publishes.
/// </remarks>
public class Enrolment : Entity
{
    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public Guid ProgrammeId { get; set; }

    public Programme Programme { get; set; } = null!;

    /// <summary>A term code, e.g. <c>2026-T3</c>.</summary>
    public required string Term { get; set; }

    public EnrolmentStatus Status { get; set; } = EnrolmentStatus.Pending;

    public DateTimeOffset EnrolledAt { get; set; }
}

/// <summary>
/// One scheduled session — a Saturday morning, a workshop, a camp day.
/// </summary>
/// <remarks>
/// Named <c>TutoringSession</c> rather than <c>Session</c> to avoid colliding
/// with ASP.NET Core's <c>ISession</c>, which would make every file that touched
/// both need an alias.
/// </remarks>
public class TutoringSession : Entity
{
    public Guid ProgrammeId { get; set; }

    public Programme Programme { get; set; } = null!;

    public Guid VenueId { get; set; }

    public Venue Venue { get; set; } = null!;

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    /// <summary>"Maths &amp; Physical Science" — what this Saturday covers.</summary>
    public string? SubjectFocus { get; set; }

    public string? Room { get; set; }

    public int Capacity { get; set; }

    public ICollection<AttendanceRecord> Attendance { get; set; } = [];

    public bool Contains(DateTimeOffset moment) => moment >= StartsAt && moment <= EndsAt;
}

/// <summary>
/// One learner's mark for one session.
/// </summary>
/// <remarks>
/// <para>
/// The highest-volume table in the system — roughly 214 learners × 38 Saturdays
/// a year — and the source for every attendance figure on the public Impact
/// page.
/// </para>
/// <para>
/// A unique index on (session, learner) means a learner is registered at most
/// once per session, so two staff members capturing the same register
/// simultaneously produce an error rather than two rows.
/// <see cref="RecordedBy"/> makes every entry attributable.
/// </para>
/// <para>
/// Named <c>AttendanceRecord</c> because <c>Attendance</c> reads as a collective
/// noun and would be confusing as a single row.
/// </para>
/// </remarks>
public class AttendanceRecord : Entity
{
    public Guid SessionId { get; set; }

    public TutoringSession Session { get; set; } = null!;

    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public AttendanceStatus Status { get; set; }

    /// <summary>
    /// When the learner arrived. Null for an absence, and clamped to the session
    /// window on write so a mistyped or clock-skewed time cannot put an arrival
    /// two days after the session.
    /// </summary>
    public DateTimeOffset? ArrivedAt { get; set; }

    public string? Note { get; set; }

    /// <summary>The staff member who captured this mark.</summary>
    public Guid RecordedBy { get; set; }
}

/// <summary>
/// A mentor–learner pairing for one school year.
/// </summary>
/// <remarks>
/// A filtered unique index allows only one <em>active</em> assignment per
/// learner per year, so handing a child between mentors mid-year has to be an
/// explicit decision: the old assignment is ended first.
/// </remarks>
public class MentorAssignment : Entity
{
    public Guid MentorId { get; set; }

    public Mentor Mentor { get; set; } = null!;

    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public int SchoolYear { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Active;

    /// <summary>The staff member who approved the pairing. Never nullable.</summary>
    public Guid ApprovedBy { get; set; }

    public ICollection<CheckIn> CheckIns { get; set; } = [];
}

/// <summary>
/// A mentor's weekly check-in note.
/// </summary>
/// <remarks>
/// <para>
/// Hangs off the <see cref="MentorAssignment"/> rather than off the learner
/// directly, so a note is always attributable to a specific mentor–learner
/// relationship in a specific school year.
/// </para>
/// <para>
/// When <see cref="ConcernFlagged"/> is true the row becomes invisible to the
/// parent and learner roles, and to the mentor who wrote it, and is visible to
/// the designated safeguarding lead only. That filter is applied in the query,
/// not after the fetch — see <c>MentorshipRepository.GetCheckInsForLearnerAsync</c>
/// — so a parent's request never loads the note at all.
/// </para>
/// </remarks>
public class CheckIn : Entity
{
    public Guid AssignmentId { get; set; }

    public MentorAssignment Assignment { get; set; } = null!;

    public DateOnly OccurredOn { get; set; }

    public CheckInMode Mode { get; set; }

    public WellbeingRating Wellbeing { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Areas the learner needs help with. JSON, because the vocabulary changes
    /// with the programme and is never joined on.
    /// </summary>
    public List<string> SupportAreas { get; set; } = [];

    public bool ConcernFlagged { get; set; }

    /// <summary>
    /// The user the escalation chain routed this concern to. Set by
    /// <c>EscalateFlaggedConcernHandler</c>, never by the mentor.
    /// </summary>
    public Guid? EscalatedTo { get; set; }

    public DateTimeOffset? EscalatedAt { get; set; }

    /// <summary>
    /// When the safeguarding lead recorded that this concern had been dealt with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null means the concern is still open, and the safeguarding queue shows
    /// only open ones. Without this column the queue is append-only: every
    /// concern ever flagged stays in it, a lead reading it cannot tell what is
    /// outstanding from what was resolved in March, and a capped list eventually
    /// pushes the oldest <em>unresolved</em> concern off the bottom.
    /// </para>
    /// <para>
    /// Closing is a new value on the existing row rather than a deletion,
    /// because the concern itself is the safeguarding record and must survive
    /// being dealt with.
    /// </para>
    /// </remarks>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Who closed it. Recorded because a safeguarding decision has an author.</summary>
    public Guid? ClosedBy { get; set; }

    /// <summary>
    /// What was done about it, in the lead's own words.
    /// </summary>
    /// <remarks>
    /// Free text rather than an enum. An outcome like "spoke to the grandmother,
    /// referred to the clinic social worker, reviewing in two weeks" does not fit
    /// a dropdown, and forcing it into one would lose the part that matters if
    /// this record is ever needed.
    /// </remarks>
    public string? Outcome { get; set; }
}

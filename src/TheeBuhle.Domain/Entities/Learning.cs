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
// ERD view 3 of 4 — learning, assessment and gamification
// Task 1 documentation, Figure 2c.
// =============================================================================

/// <summary>Maths, Physical Science, Accounting, English HL, Life Sciences, LO.</summary>
public class Subject : Entity
{
    public required string Name { get; set; }

    public int Grade { get; set; }

    /// <summary>A single glyph used as the tile mark in the app's subject grid.</summary>
    public string? Icon { get; set; }

    /// <summary>A hex colour from the Heritage palette, for the tile swatch.</summary>
    public string? Accent { get; set; }

    public ICollection<LearningResource> Resources { get; set; } = [];
    public ICollection<AssessmentResult> Results { get; set; } = [];
}

/// <summary>
/// A worksheet, past paper, video or reading list.
/// </summary>
/// <remarks>
/// Named <c>LearningResource</c> rather than <c>Resource</c>, which collides with
/// .NET's resource concept and reads ambiguously in a codebase that also has
/// embedded resources.
/// </remarks>
public class LearningResource : Entity
{
    public Guid SubjectId { get; set; }

    public Subject Subject { get; set; } = null!;

    public required string Title { get; set; }

    /// <summary>"Trigonometry", "Euclidean geometry" — drives the topic filter.</summary>
    public string? Topic { get; set; }

    public ResourceType Type { get; set; }

    public int Grade { get; set; }

    /// <summary>
    /// A container-relative path in Blob Storage, never a public URL.
    /// </summary>
    /// <remarks>
    /// This property is stripped from every response the API returns. A client
    /// asks for a signed, short-lived download URL instead, which is the
    /// user-delegation SAS approach from Task 1 section 9.3.11.1: the file is
    /// reachable for ten minutes by whoever asked, and a leaked link is worthless
    /// shortly afterwards.
    /// </remarks>
    public required string StoragePath { get; set; }

    public long? SizeBytes { get; set; }

    public int? DurationMinutes { get; set; }

    /// <summary>Null until published. Unpublished resources are invisible to learners.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public ICollection<ResourceProgress> Progress { get; set; } = [];
}

/// <summary>
/// How far one learner has got with one resource.
/// </summary>
/// <remarks>
/// A composite primary key of (learner, resource), so "one progress row per
/// learner per resource" is a database rule rather than something application
/// code has to remember. Writes take the greater of the stored and reported
/// value, so a client replaying a stale event cannot walk a learner's progress
/// backwards.
/// </remarks>
public class ResourceProgress
{
    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public Guid ResourceId { get; set; }

    public LearningResource Resource { get; set; } = null!;

    public int PercentComplete { get; set; }

    public DateTimeOffset? LastOpenedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsComplete => PercentComplete >= 100;
}

/// <summary>
/// A mark for one learner, one subject, one term.
/// </summary>
/// <remarks>
/// <see cref="Source"/> matters: the Impact page aggregates
/// <see cref="MarkSource.SchoolReport"/> only, so the foundation is not marking
/// its own homework with its own practice tests.
/// </remarks>
public class AssessmentResult : Entity
{
    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public Guid SubjectId { get; set; }

    public Subject Subject { get; set; } = null!;

    public required string Term { get; set; }

    public decimal Mark { get; set; }

    public MarkSource Source { get; set; }

    public Guid CapturedBy { get; set; }
}

/// <summary>
/// A badge definition.
/// </summary>
/// <remarks>
/// Criteria are <em>data</em>, not code: a staff member can add "Steady hand at
/// six sessions" as a row, and only a genuinely new <em>kind</em> of rule needs
/// a code change. That is why <see cref="Criteria"/> and
/// <see cref="Threshold"/> are columns.
/// </remarks>
public class Badge : Entity
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public string? Icon { get; set; }

    public BadgeCriteria Criteria { get; set; }

    public int Threshold { get; set; } = 1;

    /// <summary>Contributes to the learner's milestone bar.</summary>
    public int Points { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    public ICollection<LearnerBadge> AwardedTo { get; set; } = [];
}

/// <summary>
/// A badge held by a learner.
/// </summary>
/// <remarks>
/// A composite primary key makes double-awarding impossible, which is what lets
/// the repository insert-and-catch instead of check-then-insert — the latter
/// races when two registers are captured at the same moment.
/// </remarks>
public class LearnerBadge
{
    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public Guid BadgeId { get; set; }

    public Badge Badge { get; set; } = null!;

    public DateTimeOffset AwardedAt { get; set; }
}

/// <summary>
/// A staff-created study group.
/// </summary>
/// <remarks>
/// <see cref="ApprovedBy"/> is not nullable: no group exists without a staff
/// approver, and joining one is a request rather than an action.
/// </remarks>
public class PeerGroup : Entity
{
    public required string Name { get; set; }

    public Guid? SubjectId { get; set; }

    public Subject? Subject { get; set; }

    public Guid? VenueId { get; set; }

    public Venue? Venue { get; set; }

    /// <summary>"Saturdays 12h30".</summary>
    public string? MeetsAt { get; set; }

    public string? Location { get; set; }

    public Guid ApprovedBy { get; set; }

    public ICollection<GroupMembership> Memberships { get; set; } = [];
}

/// <summary>
/// A learner's membership of a peer group.
/// </summary>
/// <remarks>
/// Created as <see cref="MembershipStatus.Pending"/>. An adult confirms it —
/// the same approval rule as buddy pairing.
/// </remarks>
public class GroupMembership
{
    public Guid GroupId { get; set; }

    public PeerGroup Group { get; set; } = null!;

    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;

    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>
/// One learner asking to be paired with another.
/// </summary>
/// <remarks>
/// <para>
/// The most safeguarding-sensitive relationship in the learning model, because
/// it connects two children. Three rules are expressed here rather than
/// elsewhere:
/// </para>
/// <list type="bullet">
/// <item>A request starts <see cref="BuddyRequestStatus.Pending"/>. There is no
/// constructor or method that creates an approved pairing in one step.</item>
/// <item><see cref="ApprovedBy"/> is a mentor or staff user — never one of the
/// two learners.</item>
/// <item>A CHECK constraint refuses a request where requester and recipient are
/// the same learner.</item>
/// </list>
/// <para>
/// And note what is absent from the whole model: there is no message entity
/// between learners. Not a restricted one, not a moderated one. The API has no
/// endpoint for it, so there is nothing for a client to open.
/// </para>
/// </remarks>
public class BuddyRequest : Entity
{
    public Guid RequesterId { get; set; }

    public Learner Requester { get; set; } = null!;

    public Guid RecipientId { get; set; }

    public Learner Recipient { get; set; } = null!;

    public BuddyRequestStatus Status { get; set; } = BuddyRequestStatus.Pending;

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>
/// A derived, one-way projection of a learner's attendance.
/// </summary>
/// <remarks>
/// Refreshed by an advisory event handler whenever a register is captured, and
/// read by the mobile app so a cold start renders a dashboard from one indexed
/// row instead of running the aggregate over an expensive mobile connection.
/// <para>
/// Nothing treats this as a source of truth, so a stale or missing row costs the
/// app a slower first paint and corrupts nothing. That is the property that made
/// the Firestore read model in Task 1 section 9.3.11.2 safe, and it is preserved
/// here without the second data store.
/// </para>
/// </remarks>
public class LearnerSummary
{
    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public int SessionsRecorded { get; set; }

    public int SessionsAttended { get; set; }

    public int SessionsLate { get; set; }

    public int AttendanceRate { get; set; }

    public DateTimeOffset RefreshedAt { get; set; }
}

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using TheeBuhle.Domain.Common;

namespace TheeBuhle.Domain.Entities;

// =============================================================================
// ERD view 1 of 4 — people and access
//
// Grouped by ERD view rather than one file per class, so the code is navigable
// against the Task 1 documentation: this file is Figure 2a. The entities here
// are small and only meaningful together — a GuardianLink on its own says
// nothing.
// =============================================================================

/// <summary>
/// The single authentication record for every human in the system.
/// </summary>
/// <remarks>
/// One <c>User</c> row holds credentials and role for every person; the four
/// role tables carry only role-specific attributes. That is why a role change
/// does not mean recreating an account, and why an authorisation rule can be
/// written against one identity.
/// </remarks>
public class User : Entity
{
    public required string Email { get; set; }

    /// <summary>
    /// The Argon2id hash.
    /// </summary>
    /// <remarks>
    /// This property never leaves the data layer. It is read by exactly one
    /// service (<c>AuthService</c>) and is excluded from every projection the
    /// API returns — see <c>TheeBuhleDbContext</c>, where it is the only
    /// property marked as sensitive for the audit interceptor.
    /// </remarks>
    public required string PasswordHash { get; set; }

    public Role Role { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Invited;

    public bool MfaEnabled { get; set; }

    /// <summary>Consecutive failed sign-in attempts. Reset on success.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>
    /// Set when <see cref="FailedAttempts"/> reaches the configured threshold.
    /// Paired with per-address rate limiting, so an attacker is constrained both
    /// by where they are and by which account they are targeting.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    // Navigation. Each is at most one row: a person is a parent or a mentor, not
    // both, and the database enforces that with a unique index on user_id.
    public ParentGuardian? ParentProfile { get; set; }
    public Learner? LearnerProfile { get; set; }
    public Mentor? MentorProfile { get; set; }
    public StaffMember? StaffProfile { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<DeviceToken> DeviceTokens { get; set; } = [];
    public ICollection<NotificationPreference> NotificationPreferences { get; set; } = [];
}

/// <summary>
/// A child or young person on one of the programmes.
/// </summary>
public class Learner : Entity
{
    /// <summary>
    /// Nullable: primary-school learners have no login. The record exists from
    /// enrolment; an account is added later if they are old enough.
    /// </summary>
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// The public pseudonym, e.g. <c>A-214</c>.
    /// </summary>
    /// <remarks>
    /// This is what appears on the Sponsor a Child cards, in the safeguarding
    /// queue and in every log line — so the website never renders a child's real
    /// name, and a log file never identifies one.
    /// </remarks>
    public required string ReferenceCode { get; set; }

    public required string FirstName { get; set; }

    public required string Surname { get; set; }

    public DateOnly DateOfBirth { get; set; }

    public int Grade { get; set; }

    public string? School { get; set; }

    public string? Suburb { get; set; }

    /// <summary>
    /// A line shown on the public Sponsor a Child card — an ambition, not an
    /// identity. "Wants to study civil engineering" rather than anything that
    /// could identify the child.
    /// </summary>
    public string? Ambition { get; set; }

    /// <summary>
    /// Whether a guardian has consented in writing to photography. False by
    /// default, and the site publishes no learner photographs regardless.
    /// </summary>
    public bool PhotoConsent { get; set; }

    public ICollection<GuardianLink> GuardianLinks { get; set; } = [];
    public ICollection<Enrolment> Enrolments { get; set; } = [];
    public ICollection<AttendanceRecord> Attendance { get; set; } = [];
    public ICollection<MentorAssignment> MentorAssignments { get; set; } = [];
    public ICollection<AssessmentResult> Results { get; set; } = [];
    public ICollection<LearnerBadge> Badges { get; set; } = [];
    public ICollection<ResourceProgress> ResourceProgress { get; set; } = [];

    public string FullName => $"{FirstName} {Surname}";
}

/// <summary>
/// A parent or guardian. Their contact details are the emergency contact of
/// record, so a change is written to the audit log.
/// </summary>
public class ParentGuardian : Entity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public required string FirstName { get; set; }

    public required string Surname { get; set; }

    public required string Phone { get; set; }

    public string? AltPhone { get; set; }

    public string Relationship { get; set; } = "guardian";

    /// <summary>
    /// When the guardian last confirmed these details are current. Surfaced in
    /// the app so an out-of-date emergency number is visible rather than assumed
    /// correct.
    /// </summary>
    public DateOnly? ContactConfirmedAt { get; set; }

    public ICollection<GuardianLink> GuardianLinks { get; set; } = [];
    public ICollection<Consent> Consents { get; set; } = [];

    public string FullName => $"{FirstName} {Surname}";
}

/// <summary>
/// The junction resolving "which children may this parent see".
/// </summary>
/// <remarks>
/// A junction because a parent may have several children on the programme and a
/// child may have two guardians. More importantly: <strong>this table is the
/// only thing the authorisation check consults.</strong> There is no "or the
/// surname matches", no "or the child has no other guardian". A parent sees a
/// child because a staff member created a link row, full stop.
/// </remarks>
public class GuardianLink
{
    public Guid ParentId { get; set; }

    public ParentGuardian Parent { get; set; } = null!;

    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    /// <summary>The guardian contacted first. Orders a parent's learner list.</summary>
    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A vetted adult who commits to a learner for a full school year.
/// </summary>
public class Mentor : Entity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public required string FirstName { get; set; }

    public required string Surname { get; set; }

    public string? Occupation { get; set; }

    public ClearanceStatus ClearanceStatus { get; set; } = ClearanceStatus.None;

    /// <summary>
    /// When the police clearance lapses.
    /// </summary>
    /// <remarks>
    /// A database CHECK insists that a <see cref="ClearanceStatus.Cleared"/>
    /// mentor has a date here, but it cannot compare that date to today — so the
    /// <em>expiry</em> half of the rule is enforced by
    /// <c>MentorshipService.AssertClearedAsync</c>, and no assignment can be
    /// created without passing through it.
    /// </remarks>
    public DateOnly? ClearanceExpiry { get; set; }

    public ICollection<MentorAssignment> Assignments { get; set; } = [];

    public string FullName => $"{FirstName} {Surname}";

    /// <summary>Whether this mentor may be assigned to a learner today.</summary>
    public bool IsClearedOn(DateOnly date) =>
        ClearanceStatus == ClearanceStatus.Cleared
        && ClearanceExpiry is { } expiry
        && expiry > date;
}

/// <summary>
/// A member of the foundation's staff.
/// </summary>
/// <remarks>
/// Named <c>StaffMember</c> rather than <c>Staff</c> because the plural reads
/// wrongly as a single entity, and <c>Staff</c> as a DbSet name would then be
/// ambiguous about whether it held one person or the team.
/// </remarks>
public class StaffMember : Entity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public required string FirstName { get; set; }

    public required string Surname { get; set; }

    public required string JobTitle { get; set; }

    /// <summary>
    /// The designated safeguarding lead.
    /// </summary>
    /// <remarks>
    /// A filtered unique index enforces that at most one staff row carries this
    /// flag, so there is never any doubt about who is responsible. On SQL Server
    /// that is a real <c>CREATE UNIQUE INDEX ... WHERE</c>; the MySQL reference
    /// implementation had to emulate it with a generated column.
    /// </remarks>
    public bool IsSafeguardingLead { get; set; }

    public string FullName => $"{FirstName} {Surname}";
}

/// <summary>
/// An opaque refresh token, stored only as a SHA-256 hash.
/// </summary>
/// <remarks>
/// Rotated on every use. Presenting an already-revoked token revokes the whole
/// family for that user, which is what makes a stolen token <em>detectable</em>
/// rather than merely usable.
/// </remarks>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>SHA-256 of the token. The token itself is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Free text from the client, e.g. "Thee Buhle app". For the user's benefit.</summary>
    public string? Device { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>
/// One installed app per user, for push delivery.
/// </summary>
/// <remarks>
/// A revoked token is kept rather than deleted, so a reinstall is
/// distinguishable from a first install when support has to explain why a notice
/// was not received.
/// </remarks>
public class DeviceToken : Entity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public required string Token { get; set; }

    public DevicePlatform Platform { get; set; }

    public string? AppVersion { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// A one-time code that lets somebody set their password: an invitation to a
/// new account, or a reset of a forgotten password.
/// </summary>
/// <remarks>
/// <para>
/// There is no public sign-up. Accounts are created by staff, because an
/// account here is a view of a child's records, and the foundation must decide
/// who is whose parent. The person then proves they received the code and
/// chooses their own password, so staff never know it.
/// </para>
/// <para>
/// Only a SHA-256 hash of the code is stored, as with refresh tokens: a copy of
/// the database must not be a set of working invitations. Codes expire, work
/// once, and stop working after <see cref="MaxAttempts"/> wrong guesses.
/// </para>
/// </remarks>
public class AccountCode : Entity
{
    /// <summary>Wrong guesses allowed before the code is dead.</summary>
    public const int MaxAttempts = 5;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public AccountCodePurpose Purpose { get; set; }

    /// <summary>Hex SHA-256 of the normalised code. Never the code itself.</summary>
    public required string CodeHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>Set when a newer code for the same person replaces this one.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public int FailedAttempts { get; set; }

    /// <summary>The staff member who issued it, for the audit trail.</summary>
    public Guid IssuedBy { get; set; }

    public bool IsOpen(DateTimeOffset now) =>
        UsedAt is null && RevokedAt is null && ExpiresAt > now && FailedAttempts < MaxAttempts;
}

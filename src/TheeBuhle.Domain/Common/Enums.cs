// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

namespace TheeBuhle.Domain.Common;

/// <summary>
/// The six roles from the schema, as a type rather than a string.
/// </summary>
/// <remarks>
/// Making this an enum means a typo in a role name is a compile error, not a
/// silently failed authorisation check that quietly lets somebody through — or
/// quietly locks everybody out.
/// </remarks>
public enum Role
{
    Parent,
    Learner,
    Mentor,
    Staff,
    SafeguardingLead,
    Admin,
}

public enum UserStatus
{
    Active,
    Invited,
    Suspended,
    Deactivated,
}

/// <summary>What a one-time account code is for.</summary>
public enum AccountCodePurpose
{
    /// <summary>First sign-in: the person chooses their password.</summary>
    Invite,

    /// <summary>A forgotten password, reset by staff on request.</summary>
    PasswordReset,
}

public enum AttendanceStatus
{
    Present,
    Late,
    Absent,

    /// <summary>
    /// A kind word about <em>why</em> a learner was away. Still an absence in
    /// the figures, and it still breaks an attendance streak — the badge is for
    /// showing up.
    /// </summary>
    Excused,
}

public enum EnrolmentStatus
{
    Pending,
    Active,
    Completed,
    Withdrawn,
}

/// <summary>
/// A mentor's police-clearance state.
/// </summary>
/// <remarks>
/// South African law requires anyone working with children to be screened
/// against the National Child Protection Register. <see cref="Cleared"/> is the
/// only value that permits an assignment, and it additionally requires an expiry
/// date that has not passed — a rule the database cannot express and
/// MentorshipService therefore enforces.
/// </remarks>
public enum ClearanceStatus
{
    None,
    Pending,
    Cleared,
    Expired,
    Revoked,
}

public enum AssignmentStatus
{
    Active,
    Ended,
    Paused,
}

public enum CheckInMode
{
    InPerson,
    Phone,
    WhatsApp,
    Missed,
}

/// <summary>
/// How a mentor rates a learner at a check-in.
/// </summary>
/// <remarks>
/// <see cref="Concerned"/> flags the check-in for the safeguarding lead whether
/// or not the mentor also ticks the box. A mentor should not be able to record
/// that they are worried about a child and have it sit quietly in a note.
/// </remarks>
public enum WellbeingRating
{
    Thriving,
    Steady,
    Wobbling,
    Concerned,
}

public enum ResourceType
{
    Worksheet,
    PastPaper,
    Video,
    ReadingList,
}

/// <summary>
/// How a badge is earned. Criteria are data rather than code, so staff can add
/// a badge without a deployment; this enum names the <em>kinds</em> of rule the
/// evaluator knows how to measure.
/// </summary>
public enum BadgeCriteria
{
    AttendanceStreak,
    SessionsAttended,
    ResourcesCompleted,
    MarkImprovement,
    WorkshopsAttended,
    Manual,
}

public enum MarkSource
{
    /// <summary>
    /// Issued by the learner's school. The only source the Impact page
    /// aggregates — we do not mark our own homework.
    /// </summary>
    SchoolReport,
    InternalTest,
}

public enum MembershipStatus
{
    Joined,
    Pending,
    Left,
}

public enum BuddyRequestStatus
{
    Pending,
    Approved,
    Declined,
    Withdrawn,
}

public enum DonationFrequency
{
    Once,
    Monthly,
    Sponsor,
}

public enum DonationStatus
{
    Pending,
    Complete,
    Failed,
    Refunded,
    Cancelled,
}

public enum EventType
{
    Fundraising,
    Workshop,
    OpenDay,
    Community,
}

public enum VolunteerStatus
{
    Submitted,
    Screening,
    Cleared,
    Declined,
    Withdrawn,
}

public enum ComplianceDocumentType
{
    IdCopy,
    PoliceClearance,
    Reference,
    SafeguardingPolicy,
    Qualification,
}

public enum AnnouncementCategory
{
    PermissionForm,
    ProgrammeUpdate,
    Event,
    General,
    Safeguarding,
}

public enum Audience
{
    All,
    Parents,
    Learners,
    Mentors,
    Staff,
}

/// <summary>
/// A parent's decision on a permission form.
/// </summary>
/// <remarks>
/// <see cref="Withdrawn"/> is recorded as a new decision, never as an edit of
/// the granted one. POPIA requires the full consent history to stay
/// reconstructible, so the consent log is append-only.
/// </remarks>
public enum ConsentDecision
{
    Granted,
    Declined,
    Withdrawn,
}

public enum NotificationCategory
{
    SessionReminder,
    Events,
    Learning,
    Deadlines,
    Announcements,
}

/// <summary>
/// How a notification is delivered.
/// </summary>
/// <remarks>
/// <see cref="InApp"/> is the floor: it cannot fail for a reason outside the
/// system, which is why it is the fallback. <see cref="Sms"/> costs the
/// foundation money per message and is only ever used where a parent has chosen
/// it — never as an automatic fallback.
/// </remarks>
public enum DeliveryChannel
{
    Push,
    Sms,
    Email,
    InApp,
}

public enum DeliveryStatus
{
    Queued,
    Sent,
    Failed,
    Suppressed,
}

public enum NewsCategory
{
    SuccessStory,
    ProgrammeUpdate,
    Community,
    Announcement,
    Press,
}

public enum ContactTopic
{
    General,
    Enrol,
    Partnership,
    Media,
    Volunteering,

    /// <summary>
    /// A child-safety report. Routed to the designated safeguarding lead's
    /// mailbox and never to the general enquiries inbox.
    /// </summary>
    Concern,
}

public enum ContactStatus
{
    New,
    InProgress,
    Answered,
    Closed,
}

public enum DevicePlatform
{
    Ios,
    Android,
    Web,
}

/// <summary>
/// Role predicates used by the authorisation policy.
/// </summary>
/// <remarks>
/// Extension methods rather than properties on the enum, so the rules live
/// together and read as sentences at the call site.
/// </remarks>
public static class RoleExtensions
{
    /// <summary>Staff-side roles see programme-wide data, not just their own.</summary>
    public static bool IsStaffSide(this Role role) =>
        role is Role.Staff or Role.SafeguardingLead or Role.Admin;

    /// <summary>
    /// May read a check-in flagged as a safeguarding concern.
    /// </summary>
    /// <remarks>
    /// Task 1 section 9.3.13.7: a flagged note is visible to the safeguarding
    /// lead only. Not to the parent, not to the learner, and <em>not to ordinary
    /// programme staff either</em> — the point of the flag is that programme
    /// staff may be the subject of it. <see cref="Role.Mentor"/> is absent even
    /// though a mentor writes the note: once flagged, a concern belongs to the
    /// safeguarding process.
    /// </remarks>
    public static bool MayReadFlaggedConcerns(this Role role) =>
        role is Role.SafeguardingLead or Role.Admin;

    public static string Label(this Role role) => role switch
    {
        Role.Parent => "Parent or guardian",
        Role.Learner => "Learner",
        Role.Mentor => "Mentor",
        Role.Staff => "Programme staff",
        Role.SafeguardingLead => "Safeguarding lead",
        Role.Admin => "Administrator",
        _ => role.ToString(),
    };
}

public static class AttendanceStatusExtensions
{
    /// <summary>Whether this mark counts as the learner having turned up.</summary>
    public static bool CountsAsAttended(this AttendanceStatus status) =>
        status is AttendanceStatus.Present or AttendanceStatus.Late;
}

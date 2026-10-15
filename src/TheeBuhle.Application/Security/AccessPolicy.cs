// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Security;

/// <summary>
/// Every "may this person see that child?" decision in the system.
/// </summary>
/// <remarks>
/// <para>
/// There is one of these and it is consulted by both front ends through the same
/// API, which is the property Task 1 section 9.3.12.2 gave as the reason for one
/// API behind two clients: an authorisation rule is written once and cannot
/// drift between the website and the app.
/// </para>
/// <para>The rules, in one place:</para>
/// <list type="bullet">
/// <item>A parent may read a learner if and only if a guardian link exists.</item>
/// <item>A learner may read themselves.</item>
/// <item>A mentor may read a learner they hold an active assignment for.</item>
/// <item>Staff-side roles may read any learner, because their job is the programme.</item>
/// <item>A flagged safeguarding concern is readable by the designated lead and an
/// administrator, and by nobody else — including the mentor who wrote it, and
/// including programme staff, who may be its subject.</item>
/// </list>
/// <para>
/// Every method returns a bool. Nothing here throws and nothing here writes, so
/// a caller cannot accidentally get a side effect out of an authorisation check.
/// </para>
/// </remarks>
public sealed class AccessPolicy(
    ILearnerRepository learners,
    IMentorshipRepository mentorships)
{
    public async Task<bool> CanReadLearnerAsync(
        AuthenticatedUser user,
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        if (user.IsStaffSide)
        {
            return true;
        }

        return user.Role switch
        {
            Role.Parent when user.ParentId is { } parentId =>
                await learners.IsGuardianOfAsync(parentId, learnerId, cancellationToken),

            Role.Learner => user.LearnerId == learnerId,

            Role.Mentor when user.MentorId is { } mentorId =>
                await HasActiveAssignmentAsync(mentorId, learnerId, cancellationToken),

            _ => false,
        };
    }

    /// <summary>
    /// Writing to a learner's record is narrower than reading it. A parent may
    /// read their child's attendance and may not amend it; only staff capture and
    /// correct a register.
    /// </summary>
    public static bool CanWriteLearnerRecord(AuthenticatedUser user) => user.IsStaffSide;

    /// <summary>Capturing or amending a register is staff work.</summary>
    public static bool CanCaptureAttendance(AuthenticatedUser user) => user.IsStaffSide;

    /// <summary>
    /// A mentor may log a check-in only against their own active assignment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The assignment is the unit of authorisation, not the learner, which is why
    /// a check-in hangs off it in the schema.
    /// </para>
    /// <para>
    /// <strong>Staff are not exempt here, unlike every other read in this
    /// class.</strong> This method used to begin with the usual
    /// <c>if (user.IsStaffSide) return true;</c> shortcut, copied from
    /// <see cref="CanReadLearnerAsync"/>, and a test asserting "nobody but a
    /// mentor may log a check-in" is what showed it up.
    /// </para>
    /// <para>
    /// It was wrong for a specific reason. A check-in is attributed to the
    /// <em>mentor</em>, through the assignment it hangs off — so a check-in
    /// written by a staff member appears in the learner's record as that
    /// mentor's note, and if it is flagged it escalates as though the mentor
    /// raised the concern. Misattributing a safeguarding record is worse than
    /// being unable to write one.
    /// </para>
    /// <para>
    /// Nothing was exposing it: <c>MentorController</c> is mentor-only, so the
    /// branch was unreachable. An unreachable over-permission is still worth
    /// removing, because the next endpoint that consults this method would
    /// inherit it silently.
    /// </para>
    /// </remarks>
    public async Task<bool> CanLogCheckInAsync(
        AuthenticatedUser user,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        if (user.Role != Role.Mentor || user.MentorId is not { } mentorId)
        {
            return false;
        }

        var assignment = await mentorships.FindAssignmentAsync(assignmentId, cancellationToken);

        return assignment is not null
            && assignment.MentorId == mentorId
            && assignment.Status == AssignmentStatus.Active;
    }

    /// <summary>
    /// The safeguarding rule from Task 1 section 9.3.13.7, as one method.
    /// </summary>
    public static bool CanReadFlaggedConcerns(AuthenticatedUser user) => user.MayReadFlaggedConcerns;

    /// <summary>Approving a buddy pairing or a peer-group membership.</summary>
    public static bool CanApprovePairing(AuthenticatedUser user) =>
        user.IsStaffSide || user.Role == Role.Mentor;

    public static bool CanPublishAnnouncement(AuthenticatedUser user) => user.IsStaffSide;

    /// <summary>
    /// Only the guardian of the learner may record a consent decision, and only
    /// for their own child.
    /// </summary>
    /// <remarks>
    /// Staff cannot sign on a parent's behalf — not even an administrator. A
    /// consent record a guardian did not create is worthless as evidence that
    /// they consented.
    /// </remarks>
    public async Task<bool> CanSignConsentForAsync(
        AuthenticatedUser user,
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        if (user.Role != Role.Parent || user.ParentId is not { } parentId)
        {
            return false;
        }

        return await learners.IsGuardianOfAsync(parentId, learnerId, cancellationToken);
    }

    /// <summary>
    /// Sponsorship records: the only place an external party is linked to an
    /// identified child, so the most restricted read in the system.
    /// </summary>
    public static bool CanReadSponsorships(AuthenticatedUser user) =>
        user.Is(Role.Admin, Role.SafeguardingLead);

    public static bool CanManageAccounts(AuthenticatedUser user) => user.Role == Role.Admin;

    public static bool CanReadAuditLog(AuthenticatedUser user) =>
        user.Is(Role.Admin, Role.SafeguardingLead);

    public static bool CanVetVolunteers(AuthenticatedUser user) => user.IsStaffSide;

    private async Task<bool> HasActiveAssignmentAsync(
        Guid mentorId,
        Guid learnerId,
        CancellationToken cancellationToken)
    {
        var mentees = await learners.LearnerIdsForMentorAsync(mentorId, cancellationToken);

        return mentees.Contains(learnerId);
    }
}

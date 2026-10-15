// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using TheeBuhle.Application.Security;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Security;
using TheeBuhle.UnitTests.Fakes;

namespace TheeBuhle.UnitTests.Security;

/// <summary>
/// The authorisation matrix.
/// </summary>
/// <remarks>
/// <para>
/// This is the most important test class in the suite. Every other rule in the
/// system protects a number or a workflow; these protect children's records from
/// the wrong adults, and the failure mode is one family reading another family's
/// child.
/// </para>
/// <para>
/// The tests are written as the <em>matrix</em> rather than one per method,
/// because the interesting failures are the cells somebody forgets: not "can a
/// parent read their own child" but "can a parent read somebody else's", and not
/// "can the lead read a flagged concern" but "can the mentor who wrote it".
/// </para>
/// </remarks>
public sealed class AccessPolicyTests
{
    private readonly FakeLearnerRepository learners = new();

    private readonly FakeMentorshipRepository mentorships = new();

    private readonly AccessPolicy policy;

    public AccessPolicyTests() => policy = new AccessPolicy(learners, mentorships);

    // ------------------------------------------------------ reading a learner

    [Fact]
    public async Task A_parent_may_read_a_learner_they_are_linked_to()
    {
        var learner = learners.Add("A-214");
        var parentId = Guid.CreateVersion7();

        learners.Link(parentId, learner.Id);

        Assert.True(await policy.CanReadLearnerAsync(Parent(parentId), learner.Id));
    }

    /// <summary>
    /// The single most important assertion in the system.
    /// </summary>
    /// <remarks>
    /// Both learners exist and both parents are real, valid, signed-in users.
    /// The only thing separating them is the guardian link — which is exactly
    /// the property the production query relies on, and exactly what a
    /// well-meaning extra clause ("or the surnames match", "or the learner has
    /// no other guardian") would break.
    /// </remarks>
    [Fact]
    public async Task A_parent_may_NOT_read_another_familys_learner()
    {
        var mine = learners.Add("A-214");
        var theirs = learners.Add("A-227");

        var myParentId = Guid.CreateVersion7();
        var theirParentId = Guid.CreateVersion7();

        learners.Link(myParentId, mine.Id);
        learners.Link(theirParentId, theirs.Id);

        Assert.False(await policy.CanReadLearnerAsync(Parent(myParentId), theirs.Id));
    }

    [Fact]
    public async Task A_parent_with_no_guardian_link_may_read_nobody()
    {
        var learner = learners.Add("A-214");

        // A real parent account with no link yet — the state between an account
        // being created and staff linking a child to it.
        Assert.False(await policy.CanReadLearnerAsync(Parent(Guid.CreateVersion7()), learner.Id));
    }

    [Fact]
    public async Task A_learner_may_read_themselves()
    {
        var learner = learners.Add("A-214");

        Assert.True(await policy.CanReadLearnerAsync(Learner(learner.Id), learner.Id));
    }

    [Fact]
    public async Task A_learner_may_NOT_read_another_learner()
    {
        var me = learners.Add("A-214");
        var somebodyElse = learners.Add("A-227");

        Assert.False(await policy.CanReadLearnerAsync(Learner(me.Id), somebodyElse.Id));
    }

    [Fact]
    public async Task A_mentor_may_read_a_learner_they_hold_an_active_assignment_for()
    {
        var learner = learners.Add("A-214");
        var mentorId = Guid.CreateVersion7();

        learners.AssignMentor(mentorId, learner.Id);

        Assert.True(await policy.CanReadLearnerAsync(Mentor(mentorId), learner.Id));
    }

    [Fact]
    public async Task A_mentor_may_NOT_read_a_learner_they_are_not_assigned_to()
    {
        var mine = learners.Add("A-214");
        var notMine = learners.Add("A-227");

        var mentorId = Guid.CreateVersion7();

        learners.AssignMentor(mentorId, mine.Id);

        Assert.False(await policy.CanReadLearnerAsync(Mentor(mentorId), notMine.Id));
    }

    [Theory]
    [InlineData(Role.Staff)]
    [InlineData(Role.SafeguardingLead)]
    [InlineData(Role.Admin)]
    public async Task Staff_side_roles_may_read_any_learner(Role role)
    {
        var learner = learners.Add("A-214");

        // No guardian link, no assignment. Staff read any learner because the
        // programme is their job — which is why the *write* side is narrower.
        Assert.True(await policy.CanReadLearnerAsync(WithRole(role), learner.Id));
    }

    // ------------------------------------------------- flagged concerns

    /// <summary>
    /// The safeguarding visibility rule, as a table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two cells here are the ones that matter and are the ones a reasonable
    /// person would get wrong:
    /// </para>
    /// <list type="bullet">
    /// <item><strong>A mentor cannot read a flagged concern</strong> — not even
    /// the one they wrote. Once flagged, a concern belongs to the safeguarding
    /// process, and the author does not get to keep watching it.</item>
    /// <item><strong>Ordinary programme staff cannot either.</strong> That is
    /// the entire point of the flag: the concern may be <em>about</em> a member
    /// of programme staff.</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(Role.Parent, false)]
    [InlineData(Role.Learner, false)]
    [InlineData(Role.Mentor, false)]
    [InlineData(Role.Staff, false)]
    [InlineData(Role.SafeguardingLead, true)]
    [InlineData(Role.Admin, true)]
    public void Only_the_safeguarding_lead_and_an_admin_may_read_flagged_concerns(Role role, bool expected) =>
        Assert.Equal(expected, AccessPolicy.CanReadFlaggedConcerns(WithRole(role)));

    // --------------------------------------------------------------- consent

    /// <summary>
    /// Consent can only be recorded by a guardian of that learner.
    /// </summary>
    /// <remarks>
    /// Including, and especially, that an <strong>administrator cannot</strong>.
    /// There is no override and no "on behalf of", because a consent record a
    /// guardian did not create is worthless as evidence that they consented —
    /// which is the only reason the record exists.
    /// </remarks>
    [Theory]
    [InlineData(Role.Learner)]
    [InlineData(Role.Mentor)]
    [InlineData(Role.Staff)]
    [InlineData(Role.SafeguardingLead)]
    [InlineData(Role.Admin)]
    public async Task Nobody_except_a_guardian_may_record_a_consent_decision(Role role)
    {
        var learner = learners.Add("A-214");

        Assert.False(await policy.CanSignConsentForAsync(WithRole(role), learner.Id));
    }

    [Fact]
    public async Task A_guardian_may_record_a_consent_decision_for_their_own_child()
    {
        var learner = learners.Add("A-214");
        var parentId = Guid.CreateVersion7();

        learners.Link(parentId, learner.Id);

        Assert.True(await policy.CanSignConsentForAsync(Parent(parentId), learner.Id));
    }

    [Fact]
    public async Task A_guardian_may_NOT_record_a_consent_decision_for_another_familys_child()
    {
        var mine = learners.Add("A-214");
        var theirs = learners.Add("A-227");

        var parentId = Guid.CreateVersion7();

        learners.Link(parentId, mine.Id);

        Assert.False(await policy.CanSignConsentForAsync(Parent(parentId), theirs.Id));
    }

    // ------------------------------------------------------ writes and reads

    /// <summary>
    /// Reading a learner's record and amending it are different permissions.
    /// </summary>
    /// <remarks>
    /// A parent may read their child's attendance and may not change it. Without
    /// that split, "can see it" would imply "can edit it", and a parent could
    /// mark their own child present.
    /// </remarks>
    [Theory]
    [InlineData(Role.Parent, false)]
    [InlineData(Role.Learner, false)]
    [InlineData(Role.Mentor, false)]
    [InlineData(Role.Staff, true)]
    [InlineData(Role.SafeguardingLead, true)]
    [InlineData(Role.Admin, true)]
    public void Only_staff_may_capture_attendance(Role role, bool expected) =>
        Assert.Equal(expected, AccessPolicy.CanCaptureAttendance(WithRole(role)));

    /// <summary>
    /// Sponsorship records are the only place an external party is linked to an
    /// identified child, so they are the most restricted read in the system.
    /// </summary>
    /// <remarks>
    /// Note that ordinary programme staff cannot read them. A staff member does
    /// not need to know which donor funds which child in order to run a
    /// Saturday session, and that link is exactly what the public sponsor cards
    /// are shaped to avoid revealing.
    /// </remarks>
    [Theory]
    [InlineData(Role.Parent, false)]
    [InlineData(Role.Mentor, false)]
    [InlineData(Role.Staff, false)]
    [InlineData(Role.SafeguardingLead, true)]
    [InlineData(Role.Admin, true)]
    public void Sponsorship_records_are_the_most_restricted_read(Role role, bool expected) =>
        Assert.Equal(expected, AccessPolicy.CanReadSponsorships(WithRole(role)));

    [Theory]
    [InlineData(Role.Staff, false)]
    [InlineData(Role.SafeguardingLead, false)]
    [InlineData(Role.Admin, true)]
    public void Only_an_admin_may_manage_accounts(Role role, bool expected) =>
        Assert.Equal(expected, AccessPolicy.CanManageAccounts(WithRole(role)));

    /// <summary>
    /// The audit log: the safeguarding lead and administrators.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lead is included because a safeguarding investigation may need to
    /// answer "who opened this child's record, and when?", and they are the
    /// designated child-protection officer.
    /// </para>
    /// <para>
    /// This test exists because the policy and the route said different things.
    /// The policy permitted the lead; the controller carried
    /// <c>[RequireAdmin]</c> and refused them with a 403. A policy method
    /// nothing consults is worse than no policy method, so the route was
    /// corrected to match — and this asserts the agreed answer so the two cannot
    /// drift again silently.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(Role.Parent, false)]
    [InlineData(Role.Mentor, false)]
    [InlineData(Role.Staff, false)]
    [InlineData(Role.SafeguardingLead, true)]
    [InlineData(Role.Admin, true)]
    public void The_audit_log_is_readable_by_the_safeguarding_lead_and_admins(Role role, bool expected) =>
        Assert.Equal(expected, AccessPolicy.CanReadAuditLog(WithRole(role)));

    // -------------------------------------------------------- check-in writes

    [Fact]
    public async Task A_mentor_may_log_a_check_in_against_their_own_active_assignment()
    {
        var learner = learners.Add("A-214");
        var mentor = mentorships.AddMentor();
        var assignment = mentorships.AddAssignment(mentor, learner, 2026);

        Assert.True(await policy.CanLogCheckInAsync(Mentor(mentor.Id), assignment.Id));
    }

    [Fact]
    public async Task A_mentor_may_NOT_log_a_check_in_against_another_mentors_assignment()
    {
        var learner = learners.Add("A-214");
        var theirMentor = mentorships.AddMentor();
        var assignment = mentorships.AddAssignment(theirMentor, learner, 2026);

        Assert.False(await policy.CanLogCheckInAsync(Mentor(Guid.CreateVersion7()), assignment.Id));
    }

    /// <summary>
    /// An ended assignment cannot take new check-ins.
    /// </summary>
    /// <remarks>
    /// Worth asserting separately from the ownership rule: a mentor whose
    /// assignment was ended — because the learner was moved, or because the
    /// mentor's clearance lapsed — is still the assignment's mentor, so an
    /// ownership-only check would let them keep writing notes about a child they
    /// no longer work with.
    /// </remarks>
    [Fact]
    public async Task A_mentor_may_NOT_log_a_check_in_against_an_ended_assignment()
    {
        var learner = learners.Add("A-214");
        var mentor = mentorships.AddMentor();
        var assignment = mentorships.AddAssignment(mentor, learner, 2026, AssignmentStatus.Ended);

        Assert.False(await policy.CanLogCheckInAsync(Mentor(mentor.Id), assignment.Id));
    }

    [Theory]
    [InlineData(Role.Parent)]
    [InlineData(Role.Learner)]
    [InlineData(Role.Staff)]
    public async Task Nobody_but_a_mentor_may_log_a_check_in(Role role)
    {
        var learner = learners.Add("A-214");
        var mentor = mentorships.AddMentor();
        var assignment = mentorships.AddAssignment(mentor, learner, 2026);

        Assert.False(await policy.CanLogCheckInAsync(WithRole(role), assignment.Id));
    }

    // ------------------------------------------------------------- builders

    private static AuthenticatedUser Parent(Guid parentId) =>
        new(Guid.CreateVersion7(), "parent@test", Role.Parent, ParentId: parentId, DisplayName: "A Parent");

    private static AuthenticatedUser Learner(Guid learnerId) =>
        new(Guid.CreateVersion7(), "learner@test", Role.Learner, LearnerId: learnerId, DisplayName: "A Learner");

    private static AuthenticatedUser Mentor(Guid mentorId) =>
        new(Guid.CreateVersion7(), "mentor@test", Role.Mentor, MentorId: mentorId, DisplayName: "A Mentor");

    /// <summary>
    /// A user in one role, with every profile id populated.
    /// </summary>
    /// <remarks>
    /// All four ids are set on purpose. A policy that accidentally checked
    /// <c>ParentId is not null</c> instead of the role would pass a
    /// role-specific test built with only the matching id set, and fail here —
    /// which is the point of building the adversarial case rather than the
    /// convenient one.
    /// </remarks>
    private static AuthenticatedUser WithRole(Role role) =>
        new(
            Guid.CreateVersion7(),
            $"{role}@test".ToLowerInvariant(),
            role,
            ParentId: Guid.CreateVersion7(),
            LearnerId: Guid.CreateVersion7(),
            MentorId: Guid.CreateVersion7(),
            StaffId: Guid.CreateVersion7(),
            DisplayName: role.ToString());
}

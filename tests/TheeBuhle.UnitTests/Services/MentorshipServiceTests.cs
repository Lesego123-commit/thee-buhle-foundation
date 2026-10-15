// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Security;
using TheeBuhle.UnitTests.Fakes;

namespace TheeBuhle.UnitTests.Services;

/// <summary>
/// Mentorship: assigning a mentor, and logging a check-in.
/// </summary>
/// <remarks>
/// <para>
/// The rule that shapes this service is a legal one. South African law requires
/// anyone working with children to be screened against the National Child
/// Protection Register. A database CHECK constraint can insist that a cleared
/// mentor <em>has</em> an expiry date — and cannot compare that date to today,
/// because a CHECK may not reference the current date.
/// </para>
/// <para>
/// So the expiry half of the rule lives in the service, and these tests are the
/// only thing standing behind it.
/// </para>
/// </remarks>
public sealed class MentorshipServiceTests
{
    private readonly FakeMentorshipRepository mentorships = new();

    private readonly FakeLearnerRepository learners = new();

    private readonly FakeAuditRepository audit = new();

    private readonly FakeEventDispatcher events = new();

    private readonly FakeUnitOfWork unitOfWork = new();

    private readonly FakeClock clock = new();

    private readonly MentorshipService service;

    private readonly Learner learner;

    public MentorshipServiceTests()
    {
        learner = learners.Add("A-214", firstName: "Lebo", surname: "Mahlangu");

        service = new MentorshipService(
            mentorships,
            audit,
            events,
            unitOfWork,
            new FakeRequestContext(),
            clock);
    }

    // ------------------------------------------------------ police clearance

    [Fact]
    public async Task A_cleared_mentor_with_a_current_certificate_may_be_assigned()
    {
        var mentor = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today.AddMonths(8));

        var assignmentId = await service.AssignAsync(Admin, mentor.Id, learner.Id);

        Assert.NotEqual(Guid.Empty, assignmentId);
        Assert.Single(mentorships.Added);
    }

    /// <summary>
    /// A mentor whose clearance has expired cannot be assigned.
    /// </summary>
    /// <remarks>
    /// <strong>The half the database cannot enforce.</strong> The mentor's row is
    /// perfectly valid — status "cleared", an expiry date present — and satisfies
    /// the CHECK constraint completely. Only comparing the date to today catches
    /// it, and this service is the only place that happens.
    /// </remarks>
    [Fact]
    public async Task A_mentor_whose_clearance_has_expired_cannot_be_assigned()
    {
        var mentor = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today.AddDays(-1));

        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.AssignAsync(Admin, mentor.Id, learner.Id));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(mentorships.Added);
    }

    /// <summary>
    /// A clearance expiring today is expired.
    /// </summary>
    /// <remarks>
    /// The boundary, asserted deliberately. A certificate valid "until" a date
    /// is a document somebody has to renew, and treating the last day as still
    /// valid is the kind of off-by-one nobody notices until an audit — where the
    /// answer has to be "no mentor was ever assigned on an expired clearance".
    /// </remarks>
    [Fact]
    public async Task A_clearance_expiring_today_is_treated_as_expired()
    {
        var mentor = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today);

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.AssignAsync(Admin, mentor.Id, learner.Id));
    }

    [Theory]
    [InlineData(ClearanceStatus.None)]
    [InlineData(ClearanceStatus.Pending)]
    [InlineData(ClearanceStatus.Expired)]
    [InlineData(ClearanceStatus.Revoked)]
    public async Task A_mentor_who_is_not_cleared_cannot_be_assigned(ClearanceStatus status)
    {
        var mentor = mentorships.AddMentor(status, clock.Today.AddYears(1));

        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.AssignAsync(Admin, mentor.Id, learner.Id));

        Assert.Contains("not cleared", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(mentorships.Added);
    }

    [Fact]
    public async Task A_cleared_mentor_with_no_expiry_date_on_record_cannot_be_assigned()
    {
        var mentor = mentorships.AddMentor(ClearanceStatus.Cleared, expiry: null);

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.AssignAsync(Admin, mentor.Id, learner.Id));
    }

    // ------------------------------------------------- one mentor per learner

    /// <summary>
    /// A learner already assigned for this year cannot be assigned again.
    /// </summary>
    /// <remarks>
    /// Handing a child between mentors mid-year is a decision somebody has to
    /// make explicitly, so the existing assignment has to be ended first. The
    /// database refuses this too, through a filtered unique index — catching it
    /// here means the caller gets a sentence rather than a constraint violation.
    /// </remarks>
    [Fact]
    public async Task A_learner_with_an_active_mentor_this_year_cannot_be_assigned_another()
    {
        var first = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today.AddYears(1));
        var second = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today.AddYears(1));

        mentorships.AddAssignment(first, learner, clock.Today.Year);

        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.AssignAsync(Admin, second.Id, learner.Id));

        Assert.Contains("already has an active mentor", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An assignment ended last year does not block a new one.
    /// </summary>
    /// <remarks>
    /// The case a plain uniqueness rule would wrongly reject. A learner should
    /// keep a full history of who mentored them, and the "one active" rule is
    /// about the present rather than about the record.
    /// </remarks>
    [Fact]
    public async Task An_ended_assignment_does_not_block_a_new_one()
    {
        var oldMentor = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today.AddYears(1));
        var newMentor = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today.AddYears(1));

        mentorships.AddAssignment(oldMentor, learner, clock.Today.Year, AssignmentStatus.Ended);

        var assignmentId = await service.AssignAsync(Admin, newMentor.Id, learner.Id);

        Assert.NotEqual(Guid.Empty, assignmentId);
    }

    // ----------------------------------------------------------- check-ins

    [Fact]
    public async Task An_ordinary_check_in_is_saved_and_raises_no_concern_event()
    {
        var assignment = GivenAnActiveAssignment();

        var result = await service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today, CheckInMode.InPerson,
            WellbeingRating.Steady, "Working through trig.", ["exam_technique"], false);

        Assert.False(result.Escalated);
        Assert.Single(mentorships.CheckIns);
        Assert.Empty(events.Of<ConcernFlagged>());
    }

    /// <summary>
    /// A "concerned" rating escalates even with the box unticked.
    /// </summary>
    /// <remarks>
    /// A mentor should not be able to record that they are worried about a child
    /// and have it sit quietly in a note. The flag can only ever escalate, never
    /// suppress — so this asserts the rating alone is enough.
    /// </remarks>
    [Fact]
    public async Task A_concerned_rating_escalates_even_with_the_flag_unticked()
    {
        var assignment = GivenAnActiveAssignment();

        var result = await service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today, CheckInMode.Phone,
            WellbeingRating.Concerned, "Not herself today.", ["confidence"], concernFlagged: false);

        Assert.True(result.Escalated);
        Assert.True(mentorships.CheckIns[0].ConcernFlagged);
        Assert.Single(events.Of<ConcernFlagged>());
    }

    /// <summary>
    /// An urgent support area escalates at a calm rating.
    /// </summary>
    /// <remarks>
    /// A mentor who notes something about a child's home while rating them
    /// "steady" has still noted something about their home.
    /// </remarks>
    [Theory]
    [InlineData("home_situation")]
    [InlineData("safety")]
    [InlineData("abuse")]
    public async Task An_urgent_support_area_escalates_at_a_calm_rating(string area)
    {
        var assignment = GivenAnActiveAssignment();

        var result = await service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today, CheckInMode.InPerson,
            WellbeingRating.Steady, "Mentioned in passing.", [area], concernFlagged: false);

        Assert.True(result.Escalated);
    }

    /// <summary>
    /// An unrecognised support area is dropped rather than stored.
    /// </summary>
    /// <remarks>
    /// The vocabulary the safeguarding rules match on has to stay closed. A
    /// client that invents an area must not be able to store one, because a
    /// concern tagged with something the escalation rules do not recognise would
    /// be triaged as ordinary.
    /// </remarks>
    [Fact]
    public async Task An_unrecognised_support_area_is_dropped()
    {
        var assignment = GivenAnActiveAssignment();

        await service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today, CheckInMode.InPerson,
            WellbeingRating.Steady, null, ["exam_technique", "made_up_area"], false);

        Assert.Equal(["exam_technique"], mentorships.CheckIns[0].SupportAreas);
    }

    [Fact]
    public async Task A_check_in_dated_in_the_future_is_refused()
    {
        var assignment = GivenAnActiveAssignment();

        await Assert.ThrowsAsync<DomainRuleException>(() => service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today.AddDays(1), CheckInMode.InPerson,
            WellbeingRating.Steady, null, [], false));

        Assert.Empty(mentorships.CheckIns);
    }

    [Fact]
    public async Task A_check_in_against_an_ended_assignment_is_refused()
    {
        var mentor = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today.AddYears(1));
        var assignment = mentorships.AddAssignment(mentor, learner, clock.Today.Year, AssignmentStatus.Ended);

        await Assert.ThrowsAsync<DomainRuleException>(() => service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today, CheckInMode.InPerson,
            WellbeingRating.Steady, null, [], false));
    }

    /// <summary>
    /// A note's text is not copied into the audit trail.
    /// </summary>
    /// <remarks>
    /// The note exists once, in the check-in, where the visibility rules apply to
    /// it. The audit trail records <em>that</em> a check-in happened and how the
    /// learner was rated — a table nobody ever deletes from is the wrong place
    /// for a child's disclosure.
    /// </remarks>
    [Fact]
    public async Task The_audit_trail_records_the_check_in_without_the_note_text()
    {
        var assignment = GivenAnActiveAssignment();

        const string Disclosure = "Told me there is nobody at home in the evenings.";

        await service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today, CheckInMode.Phone,
            WellbeingRating.Concerned, Disclosure, ["home_situation"], true);

        Assert.NotEmpty(audit.Entries);
        Assert.False(
            audit.AnySnapshotContains("nobody at home"),
            "a note's text must never reach the audit trail");
        Assert.True(audit.AnySnapshotContains("A-214"));
    }

    /// <summary>
    /// A flagged check-in is not saved if the escalation fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The most important behaviour in this service. The escalation listener is
    /// deliberately <em>not</em> advisory, so an unroutable concern throws — and
    /// that exception has to take the check-in with it.
    /// </para>
    /// <para>
    /// A mentor who believes a child's situation has been reported when it
    /// reached nobody is the worst outcome this system can produce, and it is
    /// worse than an error message.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_flagged_check_in_is_rolled_back_when_it_cannot_be_escalated()
    {
        var assignment = GivenAnActiveAssignment();

        events.ThrowOn = new UnroutableConcernException("Nobody can receive this.");

        await Assert.ThrowsAsync<UnroutableConcernException>(() => service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today, CheckInMode.Phone,
            WellbeingRating.Concerned, "Urgent.", ["safety"], true));

        Assert.Equal(1, unitOfWork.Rollbacks);
        Assert.Equal(0, unitOfWork.Commits);
    }

    /// <summary>
    /// An ordinary check-in is unaffected by the escalation path failing.
    /// </summary>
    /// <remarks>
    /// The other half of the previous test: the rollback must be specific to a
    /// flagged concern, not a general fragility that makes every check-in
    /// dependent on the safeguarding chain being healthy.
    /// </remarks>
    [Fact]
    public async Task An_unflagged_check_in_saves_even_if_escalation_would_fail()
    {
        var assignment = GivenAnActiveAssignment();

        events.ThrowOn = new UnroutableConcernException("Nobody can receive this.");

        var result = await service.LogCheckInAsync(
            Mentor, assignment.Id, clock.Today, CheckInMode.InPerson,
            WellbeingRating.Thriving, "Doing well.", ["exam_technique"], false);

        Assert.False(result.Escalated);
        Assert.Single(mentorships.CheckIns);
        Assert.Equal(1, unitOfWork.Commits);
    }

    private MentorAssignment GivenAnActiveAssignment()
    {
        var mentor = mentorships.AddMentor(ClearanceStatus.Cleared, clock.Today.AddYears(1));

        return mentorships.AddAssignment(mentor, learner, clock.Today.Year);
    }

    private static AuthenticatedUser Admin => new(
        Guid.CreateVersion7(), "admin@test", Role.Admin,
        StaffId: Guid.CreateVersion7(), DisplayName: "An Administrator");

    private static AuthenticatedUser Mentor => new(
        Guid.CreateVersion7(), "mentor@test", Role.Mentor,
        MentorId: Guid.CreateVersion7(), DisplayName: "A Mentor");
}

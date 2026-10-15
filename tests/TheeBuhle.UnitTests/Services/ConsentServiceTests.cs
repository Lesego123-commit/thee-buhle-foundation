// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
// =============================================================================

using Microsoft.Extensions.Logging.Abstractions;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Application.Security;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;
using TheeBuhle.UnitTests.Fakes;

namespace TheeBuhle.UnitTests.Services;

/// <summary>
/// Recording a guardian's decision on a permission form.
/// </summary>
/// <remarks>
/// <para>
/// The rule these tests protect: <strong>consent is append-only</strong>. A
/// withdrawal is a new row, never an update of the granting one. POPIA requires
/// the full history to stay reconstructible, so "what did I agree to, and when
/// did I change my mind?" has to be answerable precisely.
/// </para>
/// <para>
/// The last test in this class is a regression for a bug found by driving the
/// live API, and its comment explains what went wrong.
/// </para>
/// </remarks>
public sealed class ConsentServiceTests
{
    private readonly FakeLearnerRepository learners = new();

    private readonly FakeMentorshipRepository mentorships = new();

    private readonly FakeConsentLog communications = new();

    private readonly FakeUnitOfWork unitOfWork = new();

    private readonly FakeEventDispatcher events = new();

    private readonly FakeClock clock = new();

    private readonly ConsentService service;

    private readonly Learner learner;

    private readonly Guid parentId = Guid.CreateVersion7();

    private readonly Guid formId = Guid.CreateVersion7();

    public ConsentServiceTests()
    {
        // A distinctive surname on purpose. The fake's default is "Learner",
        // which appears as a substring of the context key "learner_reference"
        // and makes the no-name-in-the-log assertion below pass or fail for the
        // wrong reason.
        learner = learners.Add("A-214", firstName: "Lebo", surname: "Mahlangu");
        learners.Link(parentId, learner.Id);

        communications.Forms[formId] = new PermissionForm
        {
            Id = formId,
            ProgrammeId = Guid.CreateVersion7(),
            Title = "June holiday camp",
            Body = "Four days, transport and lunch included.",
            DueDate = clock.Today.AddDays(14),
            CreatedBy = Guid.CreateVersion7(),
        };

        service = new ConsentService(
            communications,
            learners,
            new FakeAuditRepository(),
            events,
            new AccessPolicy(learners, mentorships),
            new StubSigner(),
            unitOfWork,
            new FakeRequestContext(),
            clock,
            NullLogger<ConsentService>.Instance);
    }

    // ----------------------------------------------------------- the basics

    [Fact]
    public async Task A_guardian_may_grant_permission()
    {
        var result = await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted);

        Assert.Equal(ConsentDecision.Granted, result.Decision);
        Assert.Single(communications.Appended);
    }

    /// <summary>
    /// Staff cannot sign on a parent's behalf. Not even an administrator.
    /// </summary>
    /// <remarks>
    /// A consent record a guardian did not create is worthless as evidence that
    /// they consented — which is the only reason the record exists. So there is
    /// no override, and the test asserts that nothing was written rather than
    /// only that it threw.
    /// </remarks>
    [Theory]
    [InlineData(Role.Staff)]
    [InlineData(Role.SafeguardingLead)]
    [InlineData(Role.Admin)]
    public async Task Staff_cannot_record_consent_on_a_parents_behalf(Role role)
    {
        var actor = new AuthenticatedUser(
            Guid.CreateVersion7(), $"{role}@test".ToLowerInvariant(), role,
            ParentId: parentId, StaffId: Guid.CreateVersion7(), DisplayName: "Staff");

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RecordAsync(actor, formId, learner.Id, ConsentDecision.Granted));

        Assert.Empty(communications.Appended);
    }

    [Fact]
    public async Task A_parent_cannot_record_consent_for_another_familys_child()
    {
        var otherLearner = learners.Add("A-227");

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RecordAsync(Parent, formId, otherLearner.Id, ConsentDecision.Granted));

        Assert.Empty(communications.Appended);
    }

    // ------------------------------------------------------- the append rule

    /// <summary>
    /// A withdrawal is a new row, not an edit.
    /// </summary>
    /// <remarks>
    /// The whole append-only design in one assertion: after granting and then
    /// withdrawing, there are <em>two</em> rows, and the granting one is
    /// untouched. An implementation that updated the first row would pass a
    /// "current decision is withdrawn" test and fail this one.
    /// </remarks>
    [Fact]
    public async Task Withdrawing_appends_a_second_row_and_leaves_the_first_alone()
    {
        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted);

        var granted = communications.Appended[0];

        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Withdrawn);

        Assert.Equal(2, communications.Appended.Count);
        Assert.Equal(ConsentDecision.Granted, communications.Appended[0].Decision);
        Assert.Equal(ConsentDecision.Withdrawn, communications.Appended[1].Decision);

        // The same object, unmutated.
        Assert.Same(granted, communications.Appended[0]);
        Assert.Equal(ConsentDecision.Granted, granted.Decision);
    }

    [Fact]
    public async Task There_is_nothing_to_withdraw_before_anything_was_granted()
    {
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Withdrawn));

        Assert.Contains("no active permission", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(communications.Appended);
    }

    [Fact]
    public async Task A_declined_form_cannot_then_be_withdrawn()
    {
        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Declined);

        // Declined is not granted, so there is no permission in force to remove.
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Withdrawn));

        Assert.Single(communications.Appended);
    }

    /// <summary>
    /// The same decision twice is refused rather than duplicated.
    /// </summary>
    /// <remarks>
    /// A parent tapping the button twice must not write two rows, because the
    /// history <em>is</em> the record and a duplicate makes it read as though
    /// they were asked twice.
    /// </remarks>
    [Fact]
    public async Task Recording_the_same_decision_twice_is_refused()
    {
        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted);

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted));

        Assert.Single(communications.Appended);
    }

    [Fact]
    public async Task A_withdrawn_permission_may_be_granted_again()
    {
        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted);
        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Withdrawn);
        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted);

        Assert.Equal(3, communications.Appended.Count);
        Assert.Equal(ConsentDecision.Granted, communications.Current(formId, learner.Id)!.Decision);
    }

    // --------------------------------------------------------- closing dates

    /// <summary>
    /// A closed form cannot be newly granted.
    /// </summary>
    /// <remarks>
    /// Staff have already acted on the headcount — booked the bus, ordered the
    /// food — so a late yes is a conversation rather than a database write. The
    /// message says so and gives a phone number.
    /// </remarks>
    [Fact]
    public async Task A_form_that_has_closed_cannot_be_granted()
    {
        communications.Forms[formId].DueDate = clock.Today.AddDays(-1);

        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted));

        Assert.Contains("closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A closed form can still be withdrawn.
    /// </summary>
    /// <remarks>
    /// The asymmetry is deliberate and is the more important half. A parent may
    /// change their mind about an event that has not happened yet, and a system
    /// that refused a withdrawal after the form closed would put a child on a
    /// bus their guardian had tried to take them off.
    /// </remarks>
    [Fact]
    public async Task A_form_that_has_closed_can_still_be_withdrawn()
    {
        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted);

        communications.Forms[formId].DueDate = clock.Today.AddDays(-1);

        var result = await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Withdrawn);

        Assert.Equal(ConsentDecision.Withdrawn, result.Decision);
    }

    // ------------------------------------------------------------- the event

    [Fact]
    public async Task Recording_a_decision_raises_a_consent_signed_event()
    {
        await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted);

        var raised = Assert.Single(events.Of<ConsentSigned>());

        Assert.Equal(ConsentDecision.Granted, raised.Decision);

        // The pseudonym, not the name. A log written from this event must not
        // identify the child.
        Assert.Equal("A-214", raised.LearnerReference);
        Assert.DoesNotContain(
            learner.Surname,
            System.Text.Json.JsonSerializer.Serialize(raised.Context),
            StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------ the same-second bug

    /// <summary>
    /// Two decisions in the same second still resolve in the order they were made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A regression test for a real bug.</strong> It was found by driving
    /// the live API, not by reading the code.
    /// </para>
    /// <para>
    /// The stored timestamp had one-second resolution, and the "current consent"
    /// query ordered by it. A parent who granted permission and then immediately
    /// withdrew it produced two rows with <em>identical</em> timestamps — so the
    /// ordering tied, the query returned either row, and which decision counted
    /// as current was whatever the storage engine happened to hand back first.
    /// </para>
    /// <para>
    /// A parent tapping "withdraw" and the system still showing their child as
    /// permitted is not an acceptable coin flip. The general lesson is worth
    /// keeping: <em>a timestamp cannot order an append-only log</em>, because two
    /// events can share one however fine the resolution. A monotonic sequence
    /// always can.
    /// </para>
    /// <para>
    /// The clock is deliberately frozen here, which is exactly the condition the
    /// bug needed — so a return to timestamp ordering fails this test rather than
    /// passing it by luck.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Two_decisions_in_the_same_second_resolve_by_sequence_not_by_timestamp()
    {
        // The clock does not move between these two calls.
        var granted = await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Granted);
        var withdrawn = await service.RecordAsync(Parent, formId, learner.Id, ConsentDecision.Withdrawn);

        // Identical timestamps — the precondition for the original bug.
        Assert.Equal(granted.SignedAt, withdrawn.SignedAt);

        // And the withdrawal is still unambiguously the decision that stands.
        var current = communications.Current(formId, learner.Id);

        Assert.NotNull(current);
        Assert.Equal(ConsentDecision.Withdrawn, current.Decision);

        // The sequence is what makes it unambiguous, so assert it strictly
        // increases rather than only that the answer happened to be right.
        Assert.True(
            communications.Appended[1].Sequence > communications.Appended[0].Sequence,
            "the sequence must strictly increase, or the log cannot be ordered");
    }

    private AuthenticatedUser Parent => new(
        Guid.CreateVersion7(),
        "nomvula@test",
        Role.Parent,
        ParentId: parentId,
        DisplayName: "Nomvula Mahlangu");
}

/// <summary>
/// A consent log that behaves like the real one: append-only, ordered by a
/// monotonic sequence.
/// </summary>
/// <remarks>
/// The sequence is assigned here the way the database assigns it — from a
/// counter, not from the clock. A fake that ordered by timestamp would make the
/// regression test above pass while the production query was still broken, which
/// is the one thing this fake must not do.
/// </remarks>
internal sealed class FakeConsentLog : ICommunicationRepository
{
    private long nextSequence = 1;

    public Dictionary<Guid, PermissionForm> Forms { get; } = [];

    public List<Consent> Appended { get; } = [];

    public void AppendConsent(Consent consent)
    {
        consent.Sequence = nextSequence++;

        Appended.Add(consent);
    }

    public Consent? Current(Guid formId, Guid learnerId) =>
        Appended
            .Where(c => c.FormId == formId && c.LearnerId == learnerId)
            .OrderByDescending(c => c.Sequence)
            .FirstOrDefault();

    public Task<Consent?> CurrentConsentAsync(Guid formId, Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Current(formId, learnerId));

    public Task<IReadOnlyList<ConsentHistoryEntry>> ConsentHistoryAsync(Guid formId, Guid learnerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ConsentHistoryEntry> history = Appended
            .Where(c => c.FormId == formId && c.LearnerId == learnerId)
            .OrderBy(c => c.Sequence)
            .Select(c => new ConsentHistoryEntry(c.Id, c.Sequence, c.Decision, c.SignedAt, c.SignatureRef, "Nomvula Mahlangu"))
            .ToList();

        return Task.FromResult(history);
    }

    public Task<PermissionForm?> FindPermissionFormAsync(Guid formId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Forms.GetValueOrDefault(formId));

    // ---------------------------------------------------------- not exercised

    public void AddAnnouncement(Announcement announcement) => throw new NotSupportedException();

    public Task<Announcement?> FindAnnouncementAsync(Guid announcementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> AddRecipientsAsync(Guid announcementId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> CountRecipientsAsync(Guid announcementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<InboxMessage>> InboxForAsync(Guid userId, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> MarkAnnouncementReadAsync(Guid announcementId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> UnreadAnnouncementCountAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void AddPermissionForm(PermissionForm form) => Forms[form.Id] = form;

    public Task<IReadOnlyList<OutstandingForm>> OutstandingFormsForAsync(Guid learnerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>A signer that produces a deterministic, obviously fake reference.</summary>
internal sealed class StubSigner : IUrlSigner
{
    public string SignConsent(Guid formId, Guid learnerId, Guid parentId, ConsentDecision decision, DateTimeOffset signedAt, string ipAddress) =>
        $"stub:{formId:N}:{learnerId:N}:{decision}:{signedAt:O}";

    public string SignDownload(Guid resourceId, Guid userId, DateTimeOffset expiresAt) =>
        $"stub:{resourceId:N}:{userId:N}:{expiresAt:O}";

    public bool VerifyDownload(Guid resourceId, Guid userId, DateTimeOffset expiresAt, string signature) =>
        signature == SignDownload(resourceId, userId, expiresAt);
}

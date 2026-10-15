// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using Microsoft.Extensions.Logging.Abstractions;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Safeguarding;

namespace TheeBuhle.UnitTests.Safeguarding;

/// <summary>
/// The safeguarding escalation chain.
/// </summary>
/// <remarks>
/// <para>
/// The organisation's child-protection policy, encoded as three objects. These
/// tests assert the two properties that matter:
/// </para>
/// <list type="number">
/// <item><strong>The chain never silently stops.</strong> If nobody can receive
/// a concern it throws, which rolls the check-in back — rather than returning
/// null and letting a mentor believe their report went somewhere.</item>
/// <item><strong>An urgent concern skips the first two links.</strong> A concern
/// about a child's home, safety or wellbeing may be about an adult in that
/// child's life, possibly one of the adults the first two links represent.</item>
/// </list>
/// <para>
/// One test in here — <see cref="An_attendance_concern_reaches_the_programme_manager"/>
/// — exists because of a real design flaw, and its comment says so.
/// </para>
/// </remarks>
public sealed class EscalationChainTests
{
    private readonly FakeRoleDirectory directory = new();

    private readonly EscalationChain chain;

    public EscalationChainTests() =>
        chain = new EscalationChain(
            new MentorLeadHandler(directory, NullLogger<MentorLeadHandler>.Instance),
            new ProgrammeManagerHandler(directory, NullLogger<ProgrammeManagerHandler>.Instance),
            new SafeguardingLeadHandler(directory, NullLogger<SafeguardingLeadHandler>.Instance),
            NullLogger<EscalationChain>.Instance);

    // ------------------------------------------------------- ordinary routing

    [Fact]
    public async Task An_ordinary_pastoral_concern_goes_to_the_mentor_lead()
    {
        var recipient = await chain.RouteAsync(Concern(WellbeingRating.Wobbling, "confidence"));

        Assert.Equal(directory.MentorLeadId, recipient);
    }

    /// <summary>
    /// A programme concern reaches the programme manager, not the mentor lead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This test exists because the chain was broken.</strong> In the
    /// first version, <c>MentorLeadHandler</c> absorbed every non-urgent concern
    /// — so the programme manager link was <em>unreachable</em>. The chain had
    /// three links and used two, and nothing failed: concerns were routed, just
    /// always to the wrong person.
    /// </para>
    /// <para>
    /// The fix was to have the mentor lead decline the programme areas —
    /// attendance, transport, venue, sessions — not because they are too serious
    /// but because a pastoral lead <em>cannot action them</em>. A learner missing
    /// sessions because the taxi fare went up needs a programme decision, not a
    /// conversation.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("attendance")]
    [InlineData("transport")]
    [InlineData("venue")]
    [InlineData("sessions")]
    public async Task An_attendance_concern_reaches_the_programme_manager(string area)
    {
        var recipient = await chain.RouteAsync(Concern(WellbeingRating.Steady, area));

        Assert.Equal(directory.ProgrammeManagerId, recipient);
    }

    // --------------------------------------------------------------- urgency

    /// <summary>
    /// An urgent concern goes straight to the designated lead.
    /// </summary>
    /// <remarks>
    /// Both links above are available and both would normally take a concern of
    /// this shape — so this asserts they are <em>skipped</em>, not that they were
    /// absent. A concern about a child's home may be about one of the adults
    /// those links represent.
    /// </remarks>
    [Theory]
    [InlineData("home_situation")]
    [InlineData("safety")]
    [InlineData("wellbeing")]
    [InlineData("abuse")]
    public async Task An_urgent_support_area_skips_straight_to_the_safeguarding_lead(string area)
    {
        var recipient = await chain.RouteAsync(Concern(WellbeingRating.Steady, area));

        Assert.Equal(directory.SafeguardingLeadId, recipient);
    }

    /// <summary>
    /// A "concerned" rating is urgent on its own.
    /// </summary>
    /// <remarks>
    /// The support area here is <c>confidence</c>, which the mentor lead would
    /// otherwise take. A mentor who records that they are worried about a child
    /// has said something that should not be triaged by a pastoral lead first.
    /// </remarks>
    [Fact]
    public async Task A_concerned_rating_alone_skips_to_the_safeguarding_lead()
    {
        var recipient = await chain.RouteAsync(Concern(WellbeingRating.Concerned, "confidence"));

        Assert.Equal(directory.SafeguardingLeadId, recipient);
    }

    // ------------------------------------------------------------- fallbacks

    /// <summary>
    /// A vacant link is passed over rather than treated as a refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Nobody holds the mentor-lead role" is an operational gap, not a routing
    /// decision — so the concern continues down the chain instead of stopping.
    /// </para>
    /// <para>
    /// It stops at the <em>programme manager</em>, not the safeguarding lead. I
    /// expected the lead when writing this and the test disagreed, and the code
    /// is right: the programme manager declines only <em>urgent</em> concerns,
    /// not concerns outside their remit, so they act as the backstop for an
    /// ordinary pastoral matter when the pastoral lead is absent.
    /// </para>
    /// <para>
    /// That is the better behaviour. Sending a confidence concern to the
    /// designated safeguarding lead because a role is vacant would fill the
    /// child-protection queue with things that are not child-protection matters,
    /// which is how a queue stops being read.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_vacant_mentor_lead_role_passes_the_concern_to_the_programme_manager()
    {
        directory.MentorLeadId = null;

        var recipient = await chain.RouteAsync(Concern(WellbeingRating.Wobbling, "confidence"));

        Assert.Equal(directory.ProgrammeManagerId, recipient);
    }

    [Fact]
    public async Task With_both_early_links_vacant_everything_reaches_the_lead()
    {
        directory.MentorLeadId = null;
        directory.ProgrammeManagerId = null;

        var recipient = await chain.RouteAsync(Concern(WellbeingRating.Steady, "attendance"));

        Assert.Equal(directory.SafeguardingLeadId, recipient);
    }

    /// <summary>
    /// With no safeguarding lead at all, routing throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The most important assertion in this class. The exception is what rolls
    /// the check-in back, so the alternative — returning null, or falling back
    /// to any available staff member — would mean a mentor sees "check-in saved"
    /// for a concern that reached nobody and is now sitting in a table no
    /// process reads.
    /// </para>
    /// <para>
    /// A loud failure a mentor can act on beats a quiet success they cannot.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task With_no_safeguarding_lead_routing_throws_rather_than_returning_null()
    {
        directory.MentorLeadId = null;
        directory.ProgrammeManagerId = null;
        directory.SafeguardingLeadId = null;

        var exception = await Assert.ThrowsAsync<UnroutableConcernException>(
            () => chain.RouteAsync(Concern(WellbeingRating.Concerned, "safety")));

        // The message is part of the behaviour: it has to tell the mentor what
        // to do instead, because the system has just refused to help.
        Assert.Contains("NOT been saved", exception.Message, StringComparison.Ordinal);
        Assert.Contains("phone", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Even with the earlier links available, a missing lead is fatal for an
    /// urgent concern.
    /// </summary>
    /// <remarks>
    /// This is the case that would be hidden by a chain that fell back to
    /// "whoever is available": the mentor lead is present and willing, and the
    /// concern must still not go to them, because it may be about them.
    /// </remarks>
    [Fact]
    public async Task An_urgent_concern_is_refused_rather_than_downgraded_to_an_available_link()
    {
        directory.SafeguardingLeadId = null;

        await Assert.ThrowsAsync<UnroutableConcernException>(
            () => chain.RouteAsync(Concern(WellbeingRating.Concerned, "home_situation")));
    }

    // ------------------------------------------------------------- the policy

    /// <summary>
    /// The chain describes itself, in order.
    /// </summary>
    /// <remarks>
    /// Exposed as an API endpoint as well, because the organisation's
    /// child-protection policy is encoded in code and somebody has to be able to
    /// check that the code agrees with the policy without reading the code.
    /// </remarks>
    [Fact]
    public void The_chain_describes_its_three_links_in_order()
    {
        var links = chain.Describe();

        Assert.Equal(3, links.Count);
        Assert.Contains("Mentor lead", links[0], StringComparison.Ordinal);
        Assert.Contains("Programme manager", links[1], StringComparison.Ordinal);
        Assert.Contains("safeguarding lead", links[2], StringComparison.OrdinalIgnoreCase);
    }

    private static ConcernFlagged Concern(WellbeingRating wellbeing, params string[] areas) =>
        new(
            CheckInId: Guid.CreateVersion7(),
            AssignmentId: Guid.CreateVersion7(),
            LearnerId: Guid.CreateVersion7(),
            LearnerReference: "A-214",
            MentorId: Guid.CreateVersion7(),
            Wellbeing: wellbeing,
            SupportAreas: areas);
}

/// <summary>
/// A user repository that answers only the three role lookups.
/// </summary>
/// <remarks>
/// Every other member throws. That is deliberate: if a handler starts consulting
/// something other than the three named roles, the test fails loudly rather than
/// quietly receiving an empty list — which for a safeguarding routing decision
/// would look like "nobody holds this role".
/// </remarks>
internal sealed class FakeRoleDirectory : IUserRepository
{
    public Guid? MentorLeadId { get; set; } = Guid.CreateVersion7();

    public Guid? ProgrammeManagerId { get; set; } = Guid.CreateVersion7();

    public Guid? SafeguardingLeadId { get; set; } = Guid.CreateVersion7();

    public Task<UserContact?> FindMentorLeadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Contact(MentorLeadId, "Sipho"));

    public Task<UserContact?> FindProgrammeManagerAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Contact(ProgrammeManagerId, "Lerato"));

    public Task<UserContact?> FindSafeguardingLeadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Contact(SafeguardingLeadId, "Nomsa"));

    private static UserContact? Contact(Guid? id, string name) =>
        id is null ? null : new UserContact(id.Value, $"{name.ToLowerInvariant()}@test", name);

    // ------------------------------------------------------------------------
    //  Everything else throws rather than returning a default.
    // ------------------------------------------------------------------------

    public Task<TheeBuhle.Domain.Entities.User?> FindCredentialsByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<TheeBuhle.Domain.Security.AuthenticatedUser?> FindPrincipalAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> RecordFailedLoginAsync(Guid userId, int lockThreshold, int lockMinutes, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task UpdatePasswordHashAsync(Guid userId, string hash, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<TheeBuhle.Domain.Entities.User> CreateAsync(TheeBuhle.Domain.Entities.User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SetStatusAsync(Guid userId, UserStatus status, Guid actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> IdsForAudienceAsync(Audience audience, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<UserContact?> FindContactAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

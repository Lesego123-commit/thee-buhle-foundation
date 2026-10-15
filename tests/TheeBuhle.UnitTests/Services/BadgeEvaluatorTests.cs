// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using System.Reflection;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.UnitTests.Fakes;

namespace TheeBuhle.UnitTests.Services;

/// <summary>
/// The badge rules.
/// </summary>
/// <remarks>
/// <para>
/// Gamification in a programme for vulnerable children needs one constraint held
/// firmly: <strong>no leaderboard</strong>. A public ranking in a programme like
/// this publishes which children are struggling, so the evaluator returns a
/// learner's own progress and nothing comparative.
/// </para>
/// <para>
/// The last test asserts that structurally rather than by inspection.
/// </para>
/// </remarks>
public sealed class BadgeEvaluatorTests
{
    private readonly FakeBadgeRepository badges = new();

    private readonly FakeAttendanceRepository attendance = new();

    private readonly FakeLearningProgress learning = new();

    private readonly FakeClock clock = new();

    private readonly BadgeEvaluator evaluator;

    private readonly Guid learnerId = Guid.CreateVersion7();

    public BadgeEvaluatorTests() =>
        evaluator = new BadgeEvaluator(badges, attendance, learning, clock);

    // ---------------------------------------------------------- the awarding

    [Fact]
    public async Task A_badge_whose_threshold_is_met_is_awarded()
    {
        badges.Define("steady_hand", BadgeCriteria.AttendanceStreak, threshold: 4, points: 100);
        attendance.Streak = 4;

        var awarded = await evaluator.EvaluateAsync(learnerId);

        Assert.Equal("steady_hand", Assert.Single(awarded).Code);
    }

    [Fact]
    public async Task A_badge_one_short_of_its_threshold_is_not_awarded()
    {
        badges.Define("steady_hand", BadgeCriteria.AttendanceStreak, threshold: 4, points: 100);
        attendance.Streak = 3;

        Assert.Empty(await evaluator.EvaluateAsync(learnerId));
    }

    /// <summary>
    /// Every tier a learner has passed is awarded, not only the highest.
    /// </summary>
    /// <remarks>
    /// A learner who joins mid-year and attends twelve sessions in a row should
    /// receive "first ten" as well as the streak badges — they earned both. An
    /// implementation that awarded only the top tier would silently withhold the
    /// earlier ones.
    /// </remarks>
    [Fact]
    public async Task Every_tier_the_learner_has_passed_is_awarded()
    {
        badges.Define("first_ten", BadgeCriteria.SessionsAttended, threshold: 10, points: 100);
        badges.Define("long_haul", BadgeCriteria.SessionsAttended, threshold: 25, points: 250);

        attendance.Summary = new AttendanceSummary(30, 30, 0, 0, 100);

        var awarded = await evaluator.EvaluateAsync(learnerId);

        Assert.Equal(2, awarded.Count);
        Assert.Contains(awarded, b => b.Code == "first_ten");
        Assert.Contains(awarded, b => b.Code == "long_haul");
    }

    /// <summary>
    /// A badge already held is not returned as newly awarded.
    /// </summary>
    /// <remarks>
    /// The property the attendance listener depends on: it congratulates the
    /// parent for everything the call returns, so a re-award would send the same
    /// "new badge earned" notice every Saturday for the rest of the year.
    /// </remarks>
    [Fact]
    public async Task Re_evaluating_does_not_re_award_a_badge_already_held()
    {
        badges.Define("steady_hand", BadgeCriteria.AttendanceStreak, threshold: 4, points: 100);
        attendance.Streak = 6;

        var first = await evaluator.EvaluateAsync(learnerId);
        var second = await evaluator.EvaluateAsync(learnerId);

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task An_inactive_badge_definition_is_never_awarded()
    {
        badges.Define("retired", BadgeCriteria.AttendanceStreak, threshold: 1, points: 50, isActive: false);
        attendance.Streak = 40;

        Assert.Empty(await evaluator.EvaluateAsync(learnerId));
    }

    // --------------------------------------------------------- the measuring

    /// <summary>
    /// Each criteria type reads the measurement it is about.
    /// </summary>
    /// <remarks>
    /// Distinct values on purpose. If two criteria were wired to the same source
    /// — an easy copy-paste error — identical values would hide it.
    /// </remarks>
    [Fact]
    public async Task Each_criteria_type_measures_its_own_thing()
    {
        attendance.Streak = 7;
        attendance.Summary = new AttendanceSummary(20, 18, 2, 2, 90);
        learning.CompletedResources = 5;
        learning.BestImprovement = 13;

        var measured = await evaluator.MeasureAsync(learnerId);

        Assert.Equal(7, measured[BadgeCriteria.AttendanceStreak]);
        Assert.Equal(18, measured[BadgeCriteria.SessionsAttended]);
        Assert.Equal(5, measured[BadgeCriteria.ResourcesCompleted]);
        Assert.Equal(13, measured[BadgeCriteria.MarkImprovement]);
    }

    /// <summary>
    /// Sessions attended counts late arrivals.
    /// </summary>
    /// <remarks>
    /// The summary's <c>SessionsAttended</c> already includes them, and this
    /// asserts the evaluator reads that field rather than a present-only count.
    /// A learner whose taxi is late every week should not be denied a badge for
    /// showing up.
    /// </remarks>
    [Fact]
    public async Task Sessions_attended_includes_late_arrivals()
    {
        attendance.Summary = new AttendanceSummary(12, 12, 5, 0, 100);

        var measured = await evaluator.MeasureAsync(learnerId);

        Assert.Equal(12, measured[BadgeCriteria.SessionsAttended]);
    }

    // -------------------------------------------------------- the milestones

    [Fact]
    public async Task A_learner_with_no_points_is_on_the_first_tier()
    {
        var milestone = await evaluator.MilestoneAsync(learnerId);

        Assert.Equal(1, milestone.Milestone);
        Assert.Equal("Getting started", milestone.MilestoneName);
        Assert.Equal(0, milestone.Points);
    }

    [Theory]
    [InlineData(0, 1, "Getting started")]
    [InlineData(249, 1, "Getting started")]
    [InlineData(250, 2, "Finding your feet")]
    [InlineData(500, 3, "Building momentum")]
    [InlineData(1000, 4, "Going the distance")]
    [InlineData(2000, 5, "Standing tall")]
    [InlineData(9999, 5, "Standing tall")]
    public async Task The_tier_is_the_highest_one_the_points_reach(int points, int tier, string name)
    {
        badges.Points = points;

        var milestone = await evaluator.MilestoneAsync(learnerId);

        Assert.Equal(tier, milestone.Milestone);
        Assert.Equal(name, milestone.MilestoneName);
    }

    /// <summary>
    /// The top tier reports 100% rather than dividing by zero.
    /// </summary>
    /// <remarks>
    /// There is no tier above "Standing tall", so the next target equals the
    /// floor. The naive percentage calculation is a division by zero; this
    /// asserts the special case is handled rather than crashing the one screen a
    /// learner who has done everything would most like to see.
    /// </remarks>
    [Fact]
    public async Task The_top_tier_reports_full_progress_rather_than_dividing_by_zero()
    {
        badges.Points = 5000;

        var milestone = await evaluator.MilestoneAsync(learnerId);

        Assert.Equal(100, milestone.ProgressPercent);
    }

    [Fact]
    public async Task Progress_within_a_tier_is_the_fraction_of_the_way_to_the_next()
    {
        // Halfway between 250 and 500.
        badges.Points = 375;

        var milestone = await evaluator.MilestoneAsync(learnerId);

        Assert.Equal(2, milestone.Milestone);
        Assert.Equal(500, milestone.NextTarget);
        Assert.Equal(50, milestone.ProgressPercent);
    }

    /// <summary>
    /// The milestone names the nearest unearned badge, with its description.
    /// </summary>
    /// <remarks>
    /// So the app can say "2 to go for Perfect month — attend every session in a
    /// month" rather than a bare number, which tells a learner nothing about
    /// what to do next.
    /// </remarks>
    [Fact]
    public async Task The_milestone_names_the_nearest_unearned_badge()
    {
        badges.Define("first_resource", BadgeCriteria.ResourcesCompleted, threshold: 1, points: 50);
        badges.Define("ten_books", BadgeCriteria.ResourcesCompleted, threshold: 10, points: 150);

        learning.CompletedResources = 1;

        // The first is earned, so the hint should be the one still out of reach.
        await evaluator.EvaluateAsync(learnerId);

        var milestone = await evaluator.MilestoneAsync(learnerId);

        Assert.NotNull(milestone.NextBadge);
        Assert.Equal("ten_books", milestone.NextBadge.Code);
    }

    // ------------------------------------------------------ no leaderboard

    /// <summary>
    /// Nothing the evaluator returns is comparative.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted <strong>structurally</strong>, by reflecting over the public
    /// surface, rather than by reading it. The no-leaderboard rule is a design
    /// constraint from Task 1, and a constraint that lives only in a comment is
    /// one somebody will add a "top learners" endpoint beside in six months
    /// without noticing.
    /// </para>
    /// <para>
    /// This test fails the moment a member appears whose name suggests a ranking
    /// — which is a blunt instrument, and deliberately so: it should be annoying
    /// to add one.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_evaluator_exposes_nothing_comparative()
    {
        string[] forbidden = ["rank", "position", "percentile", "leaderboard", "top", "compare", "versus", "average"];

        var members = typeof(BadgeEvaluator)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Concat(typeof(MilestoneView).GetProperties().Select(property => property.Name))
            .ToList();

        var offenders = members
            .Where(name => forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The badge repository offers no way to build a ranking either.
    /// </summary>
    /// <remarks>
    /// The rule is enforced by the absence of the data access, not by a decision
    /// in a controller — so a controller <em>cannot</em> build a leaderboard
    /// however much somebody wants one. This asserts every points-related method
    /// takes a specific learner.
    /// </remarks>
    [Fact]
    public void The_badge_repository_cannot_return_points_for_more_than_one_learner()
    {
        var pointsMethods = typeof(IBadgeRepository)
            .GetMethods()
            .Where(method => method.Name.Contains("Points", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(pointsMethods);

        Assert.All(pointsMethods, method =>
            Assert.Contains(method.GetParameters(), parameter =>
                parameter.Name is not null
                && parameter.Name.Contains("learnerId", StringComparison.OrdinalIgnoreCase)));
    }
}

// =============================================================================
//  Narrow fakes for the three things the evaluator depends on.
// =============================================================================

internal sealed class FakeBadgeRepository : IBadgeRepository
{
    private readonly List<Badge> catalogue = [];

    private readonly HashSet<(Guid LearnerId, Guid BadgeId)> held = [];

    /// <summary>Set directly, so a milestone test does not have to earn its way there.</summary>
    public int Points { get; set; }

    public Badge Define(string code, BadgeCriteria criteria, int threshold, int points, bool isActive = true)
    {
        var badge = new Badge
        {
            Code = code,
            Name = code.Replace('_', ' '),
            Description = $"Reach {threshold}.",
            Criteria = criteria,
            Threshold = threshold,
            Points = points,
            IsActive = isActive,
        };

        catalogue.Add(badge);

        return badge;
    }

    public Task<IReadOnlyList<Badge>> ActiveByCriteriaAsync(BadgeCriteria criteria, CancellationToken cancellationToken = default)
    {
        // Ordered by threshold ascending, as the real repository does — which is
        // what lets the evaluator award every passed tier in one pass.
        IReadOnlyList<Badge> matching = catalogue
            .Where(b => b.Criteria == criteria && b.IsActive)
            .OrderBy(b => b.Threshold)
            .ToList();

        return Task.FromResult(matching);
    }

    public Task<IReadOnlyList<BadgeCatalogueEntry>> CatalogueForLearnerAsync(Guid learnerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BadgeCatalogueEntry> entries = catalogue
            .Where(b => b.IsActive)
            .OrderBy(b => b.Criteria)
            .ThenBy(b => b.Threshold)
            .Select(b => new BadgeCatalogueEntry(
                b.Id, b.Code, b.Name, b.Description, b.Icon, b.Criteria, b.Threshold, b.Points,
                held.Contains((learnerId, b.Id)),
                held.Contains((learnerId, b.Id)) ? DateTimeOffset.UnixEpoch : null))
            .ToList();

        return Task.FromResult(entries);
    }

    public Task<bool> AwardAsync(Guid learnerId, Guid badgeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(held.Add((learnerId, badgeId)));

    public Task<bool> HoldsAsync(Guid learnerId, Guid badgeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(held.Contains((learnerId, badgeId)));

    public Task<int> PointsForAsync(Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Points != 0
            ? Points
            : catalogue.Where(b => held.Contains((learnerId, b.Id))).Sum(b => b.Points));

    public Task<IReadOnlyList<AwardedBadge>> AwardedForAsync(Guid learnerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AwardedBadge> awarded = catalogue
            .Where(b => held.Contains((learnerId, b.Id)))
            .Select(b => new AwardedBadge(b.Id, b.Code, b.Name, b.Description, b.Icon, b.Points, DateTimeOffset.UnixEpoch))
            .ToList();

        return Task.FromResult(awarded);
    }
}

internal sealed class FakeAttendanceRepository : IAttendanceRepository
{
    public int Streak { get; set; }

    public AttendanceSummary Summary { get; set; } = AttendanceSummary.Empty;

    public List<AttendanceRecord> Added { get; } = [];

    public AttendanceRecord? Existing { get; set; }

    public Task<AttendanceRecord?> FindForSessionAndLearnerAsync(Guid sessionId, Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Existing);

    public void Add(AttendanceRecord record) => Added.Add(record);

    public Task<IReadOnlyList<AttendanceEntryView>> RecentForLearnerAsync(Guid learnerId, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttendanceEntryView>>([]);

    public Task<AttendanceSummary> SummaryForLearnerAsync(Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Summary);

    public Task<int> CurrentStreakAsync(Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Streak);

    public Task<IReadOnlyDictionary<AttendanceStatus, int>> StatusTotalsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<AttendanceStatus, int>>(new Dictionary<AttendanceStatus, int>());
}

/// <summary>
/// The two learning measurements the badge rules need.
/// </summary>
/// <remarks>
/// Four lines, because the interface is two methods. That is the payoff of
/// declaring <see cref="IReadOnlyLearningProgress"/> separately from the whole
/// learning hub — interface segregation at the size of the need.
/// </remarks>
internal sealed class FakeLearningProgress : IReadOnlyLearningProgress
{
    public int CompletedResources { get; set; }

    public int BestImprovement { get; set; }

    public Task<int> CompletedResourceCountAsync(Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CompletedResources);

    public Task<int> BestMarkImprovementAsync(Guid learnerId, int schoolYear, CancellationToken cancellationToken = default) =>
        Task.FromResult(BestImprovement);
}

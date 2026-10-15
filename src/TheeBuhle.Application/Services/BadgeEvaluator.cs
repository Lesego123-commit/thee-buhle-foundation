// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Application.Services;

/// <summary>
/// The gamification rules.
/// </summary>
/// <remarks>
/// <para>
/// Badge definitions are rows (criteria plus threshold), so this class holds the
/// <em>evaluation</em> of a criteria type and never a list of specific badges.
/// Adding "Steady hand at six sessions" is a row; adding a new <em>kind</em> of
/// criterion is a case in <see cref="MeasureAsync"/>.
/// </para>
/// <para>
/// One design rule runs through all of it, taken from the prototype's own copy:
/// "Badges are never ranked between learners. There is no leaderboard — only
/// your own milestones." So nothing here compares two learners, and there is no
/// ordering, percentile or ranking anywhere in the class or in what it returns.
/// </para>
/// </remarks>
public sealed class BadgeEvaluator(
    IBadgeRepository badges,
    IAttendanceRepository attendance,
    IReadOnlyLearningProgress learning,
    IClock clock)
{
    /// <summary>The milestone tiers the app's progress bar reads.</summary>
    private static readonly (string Name, int At)[] Tiers =
    [
        ("Getting started", 0),
        ("Finding your feet", 250),
        ("Building momentum", 500),
        ("Going the distance", 1000),
        ("Standing tall", 2000),
    ];

    /// <summary>
    /// Evaluates every criteria type for one learner and awards what is now due.
    /// </summary>
    /// <returns>
    /// Only badges awarded <em>by this call</em>. A badge the learner already
    /// held is not returned, so the caller can use the result directly as "what
    /// to congratulate them for" without checking anything itself.
    /// </returns>
    public async Task<IReadOnlyList<Badge>> EvaluateAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        var measurements = await MeasureAsync(learnerId, cancellationToken);
        var awarded = new List<Badge>();

        foreach (var (criteria, achieved) in measurements)
        {
            foreach (var badge in await badges.ActiveByCriteriaAsync(criteria, cancellationToken))
            {
                if (achieved < badge.Threshold)
                {
                    continue;
                }

                // Relies on the composite primary key rather than a
                // read-then-write, so two simultaneous registers cannot both
                // award the same badge.
                if (await badges.AwardAsync(learnerId, badge.Id, cancellationToken))
                {
                    awarded.Add(badge);
                }
            }
        }

        return awarded;
    }

    /// <summary>
    /// What this learner has actually achieved, per criteria type.
    /// </summary>
    /// <remarks>
    /// Separated from the awarding loop so the rules can be asserted directly in
    /// a unit test with no badge rows existing at all.
    /// </remarks>
    public async Task<IReadOnlyDictionary<BadgeCriteria, int>> MeasureAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        var summary = await attendance.SummaryForLearnerAsync(learnerId, cancellationToken);
        var schoolYear = clock.Today.Year;

        return new Dictionary<BadgeCriteria, int>
        {
            [BadgeCriteria.AttendanceStreak] = await attendance.CurrentStreakAsync(learnerId, cancellationToken),
            [BadgeCriteria.SessionsAttended] = summary.SessionsAttended,
            [BadgeCriteria.ResourcesCompleted] = await learning.CompletedResourceCountAsync(learnerId, cancellationToken),
            [BadgeCriteria.MarkImprovement] = await learning.BestMarkImprovementAsync(learnerId, schoolYear, cancellationToken),

            // Workshop attendance would be counted from attendance rows against
            // workshop-programme sessions. Until that programme has sessions of
            // its own this measures zero rather than guessing at a number.
            [BadgeCriteria.WorkshopsAttended] = 0,
        };
    }

    /// <summary>
    /// The milestone bar on the app's Progress screen.
    /// </summary>
    /// <remarks>
    /// Derived from badge points rather than stored, so a staff member adding a
    /// badge automatically moves the bar rather than leaving a second number to
    /// maintain by hand.
    /// </remarks>
    public async Task<MilestoneView> MilestoneAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        var points = await badges.PointsForAsync(learnerId, cancellationToken);

        var index = 0;

        for (var i = 0; i < Tiers.Length; i++)
        {
            if (points >= Tiers[i].At)
            {
                index = i;
            }
        }

        var floor = Tiers[index].At;
        var target = index + 1 < Tiers.Length ? Tiers[index + 1].At : floor;
        var span = Math.Max(1, target - floor);

        return new MilestoneView(
            Points: points,
            Milestone: index + 1,
            MilestoneName: Tiers[index].Name,
            NextTarget: target,
            ProgressPercent: target == floor ? 100 : (int)Math.Round(100.0 * (points - floor) / span),
            NextBadge: await NextBadgeForAsync(learnerId, cancellationToken));
    }

    /// <summary>
    /// The nearest unearned badge, so the app can say "2 to go for Perfect month
    /// — attend every session in a month" rather than a bare number.
    /// </summary>
    private async Task<NextBadgeView?> NextBadgeForAsync(
        Guid learnerId,
        CancellationToken cancellationToken)
    {
        var measurements = await MeasureAsync(learnerId, cancellationToken);
        var catalogue = await badges.CatalogueForLearnerAsync(learnerId, cancellationToken);

        NextBadgeView? closest = null;
        var smallestGap = int.MaxValue;

        foreach (var badge in catalogue.Where(entry => !entry.Earned))
        {
            var achieved = measurements.GetValueOrDefault(badge.Criteria);
            var gap = badge.Threshold - achieved;

            if (gap <= 0 || gap >= smallestGap)
            {
                continue;
            }

            smallestGap = gap;
            closest = new NextBadgeView(
                badge.Code,
                badge.Name,
                badge.Description,
                badge.Icon,
                achieved,
                badge.Threshold,
                gap);
        }

        return closest;
    }
}

/// <remarks>
/// Note the absence of a rank, a position or a percentile. See the remarks on
/// <see cref="BadgeEvaluator"/> — that absence is the rule, not an oversight.
/// </remarks>
public sealed record MilestoneView(
    int Points,
    int Milestone,
    string MilestoneName,
    int NextTarget,
    int ProgressPercent,
    NextBadgeView? NextBadge);

public sealed record NextBadgeView(
    string Code,
    string Name,
    string Description,
    string? Icon,
    int Achieved,
    int Threshold,
    int Remaining);

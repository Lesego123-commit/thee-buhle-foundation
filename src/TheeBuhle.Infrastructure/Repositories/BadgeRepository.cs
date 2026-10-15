// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>Badge definitions and awards.</summary>
/// <remarks>
/// Note what this repository does <strong>not</strong> offer: no method returns
/// a rank, a position or a percentile, and there is no "top learners" query. The
/// no-leaderboard rule from Task 1 is asserted structurally rather than
/// documented — a controller cannot build a leaderboard, because the data access
/// to do it does not exist.
/// </remarks>
public sealed class BadgeRepository(TheeBuhleDbContext context, IClock clock) : IBadgeRepository
{
    /// <summary>
    /// Active badge definitions of one criteria type.
    /// </summary>
    /// <remarks>
    /// Criteria are rows, not code, so staff can add "Steady hand at six
    /// sessions" without a deployment. Ordered by threshold ascending, which is
    /// what lets the evaluator award every tier a learner has passed in one pass
    /// rather than only the highest.
    /// </remarks>
    public async Task<IReadOnlyList<Badge>> ActiveByCriteriaAsync(
        BadgeCriteria criteria,
        CancellationToken cancellationToken = default) =>
        await context.Badges
            .AsNoTracking()
            .Where(badge => badge.Criteria == criteria && badge.IsActive)
            .OrderBy(badge => badge.Threshold)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The whole badge catalogue, with what this learner has earned marked.
    /// </summary>
    /// <remarks>
    /// The unearned ones are included on purpose: the app shows a learner what
    /// is still available to earn, which is the part that motivates. A list of
    /// only what they already have is a trophy cabinet, not a goal.
    /// </remarks>
    public async Task<IReadOnlyList<BadgeCatalogueEntry>> CatalogueForLearnerAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        await context.Badges
            .AsNoTracking()
            .Where(badge => badge.IsActive)
            .OrderBy(badge => badge.Criteria)
            .ThenBy(badge => badge.Threshold)
            .Select(badge => new BadgeCatalogueEntry(
                badge.Id,
                badge.Code,
                badge.Name,
                badge.Description,
                badge.Icon,
                badge.Criteria,
                badge.Threshold,
                badge.Points,
                badge.AwardedTo.Any(award => award.LearnerId == learnerId),
                badge.AwardedTo
                    .Where(award => award.LearnerId == learnerId)
                    .Select(award => (DateTimeOffset?)award.AwardedAt)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Awards a badge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check-then-insert here is a convenience, not the guarantee. The
    /// guarantee is the composite primary key on (learner, badge), which makes a
    /// repeat award <em>impossible</em> rather than merely unlikely — so the
    /// badge evaluator can run on every single attendance mark without
    /// accumulating duplicates, and two concurrent evaluations cannot both
    /// succeed.
    /// </para>
    /// <para>
    /// Returning false rather than throwing for an already-held badge is what
    /// lets the caller say "these are the new badges" and get an accurate answer:
    /// the notification that follows an award must not be sent again for a badge
    /// the learner earned three weeks ago.
    /// </para>
    /// </remarks>
    public async Task<bool> AwardAsync(
        Guid learnerId,
        Guid badgeId,
        CancellationToken cancellationToken = default)
    {
        if (await HoldsAsync(learnerId, badgeId, cancellationToken))
        {
            return false;
        }

        await context.LearnerBadges.AddAsync(
            new LearnerBadge
            {
                LearnerId = learnerId,
                BadgeId = badgeId,
                AwardedAt = clock.Now,
            },
            cancellationToken);

        return true;
    }

    public Task<bool> HoldsAsync(
        Guid learnerId,
        Guid badgeId,
        CancellationToken cancellationToken = default) =>
        context.LearnerBadges
            .AsNoTracking()
            .AnyAsync(award => award.LearnerId == learnerId && award.BadgeId == badgeId, cancellationToken);

    /// <summary>
    /// A learner's total points.
    /// </summary>
    /// <remarks>
    /// Shown to the learner and to their parent, and to nobody else. Points are
    /// a measure of a learner's own progress over time, not a basis for
    /// comparison — which is why this takes a learner id and there is no method
    /// that returns everybody's.
    /// </remarks>
    public async Task<int> PointsForAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        await context.LearnerBadges
            .AsNoTracking()
            .Where(award => award.LearnerId == learnerId)
            .SumAsync(award => award.Badge.Points, cancellationToken);

    public async Task<IReadOnlyList<AwardedBadge>> AwardedForAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        await context.LearnerBadges
            .AsNoTracking()
            .Where(award => award.LearnerId == learnerId)
            .OrderByDescending(award => award.AwardedAt)
            .Select(award => new AwardedBadge(
                award.BadgeId,
                award.Badge.Code,
                award.Badge.Name,
                award.Badge.Description,
                award.Badge.Icon,
                award.Badge.Points,
                award.AwardedAt))
            .ToListAsync(cancellationToken);
}

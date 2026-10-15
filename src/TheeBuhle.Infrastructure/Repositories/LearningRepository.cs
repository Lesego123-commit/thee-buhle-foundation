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

/// <summary>
/// The learning hub: subjects, resources and one learner's progress.
/// </summary>
/// <remarks>
/// Every method that returns progress takes the learner id as an argument.
/// There is no ambient "current learner" anywhere in this class, which is what
/// makes it structurally impossible for one learner's progress to appear on
/// another's screen because a caller forgot to pass a filter.
/// </remarks>
public sealed class LearningRepository(TheeBuhleDbContext context, IClock clock) : ILearningRepository
{
    public async Task<IReadOnlyList<SubjectCard>> SubjectsForGradeAsync(
        int grade,
        Guid? learnerId,
        CancellationToken cancellationToken = default) =>
        await context.Subjects
            .AsNoTracking()
            .Where(subject => subject.Grade == grade)
            .OrderBy(subject => subject.Name)
            .Select(subject => new SubjectCard(
                subject.Id,
                subject.Name,
                subject.Grade,
                subject.Icon,
                subject.Accent,
                subject.Resources.Count(resource => resource.PublishedAt != null),
                // Zero when nobody is signed in, rather than a count across every
                // learner. A visitor browsing the hub sees what exists, not
                // somebody else's progress through it.
                learnerId == null
                    ? 0
                    : subject.Resources.Count(resource => resource.Progress.Any(progress =>
                        progress.LearnerId == learnerId && progress.PercentComplete >= 100))))
            .ToListAsync(cancellationToken);

    public Task<Subject?> FindSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default) =>
        context.Subjects.FirstOrDefaultAsync(subject => subject.Id == subjectId, cancellationToken);

    public async Task<IReadOnlyList<ResourceCard>> ResourcesForSubjectAsync(
        Guid subjectId,
        int grade,
        Guid? learnerId,
        ResourceType? type,
        CancellationToken cancellationToken = default)
    {
        var resources = PublishedResources()
            .Where(resource => resource.SubjectId == subjectId && resource.Grade == grade);

        if (type is { } wanted)
        {
            resources = resources.Where(resource => resource.Type == wanted);
        }

        return await resources
            .OrderBy(resource => resource.Topic)
            .ThenBy(resource => resource.Title)
            .Select(ProjectResource(learnerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResourceCard>> SearchResourcesAsync(
        string query,
        int? grade,
        Guid? learnerId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var term = (query ?? string.Empty).Trim();

        if (term.Length == 0)
        {
            return [];
        }

        var resources = PublishedResources()
            .Where(resource =>
                EF.Functions.Like(resource.Title, $"%{term}%")
                || EF.Functions.Like(resource.Topic!, $"%{term}%"));

        if (grade is { } wanted)
        {
            resources = resources.Where(resource => resource.Grade == wanted);
        }

        return await resources
            .OrderBy(resource => resource.Title)
            .Take(limit)
            .Select(ProjectResource(learnerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResourceCard>> RecentlyAddedAsync(
        int grade,
        int limit,
        CancellationToken cancellationToken = default) =>
        await PublishedResources()
            .Where(resource => resource.Grade == grade)
            .OrderByDescending(resource => resource.PublishedAt)
            .Take(limit)
            .Select(ProjectResource(null))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The one part-finished resource to offer as "continue where you left off".
    /// </summary>
    /// <remarks>
    /// Strictly between 1 and 99 per cent. Zero means they opened it and did
    /// nothing, which is not worth surfacing; 100 means they finished it, and
    /// offering somebody a video they have already watched is the sort of small
    /// wrongness that makes an app feel broken.
    /// </remarks>
    public async Task<ResourceCard?> ContinueWatchingAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        await context.ResourceProgress
            .AsNoTracking()
            .Where(progress => progress.LearnerId == learnerId
                && progress.PercentComplete > 0
                && progress.PercentComplete < 100)
            .OrderByDescending(progress => progress.LastOpenedAt)
            .Select(progress => new ResourceCard(
                progress.ResourceId,
                progress.Resource.SubjectId,
                progress.Resource.Subject.Name,
                progress.Resource.Title,
                progress.Resource.Topic,
                progress.Resource.Type,
                progress.Resource.Grade,
                progress.Resource.SizeBytes,
                progress.Resource.DurationMinutes,
                progress.PercentComplete,
                progress.Resource.PublishedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<LearningResource?> FindResourceAsync(
        Guid resourceId,
        CancellationToken cancellationToken = default) =>
        context.Resources
            .Include(resource => resource.Subject)
            .FirstOrDefaultAsync(resource => resource.Id == resourceId, cancellationToken);

    /// <summary>
    /// Records how far through a resource a learner has got.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Monotonic: progress never decreases.</strong> A learner who scrubs
    /// back to the start of a video they have finished has not un-finished it.
    /// </para>
    /// <para>
    /// Without that rule, closing a completed video at the beginning would drop
    /// its percentage below 100, the completed-resource count would fall, and the
    /// badge that count earned would be silently revoked — a badge disappearing
    /// from a child's screen for no reason they could see.
    /// </para>
    /// <para>
    /// The last-opened time <em>does</em> move every call, because that is what
    /// "continue where you left off" orders by.
    /// </para>
    /// </remarks>
    public async Task RecordProgressAsync(
        Guid learnerId,
        Guid resourceId,
        int percentComplete,
        CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(percentComplete, 0, 100);

        var existing = await context.ResourceProgress
            .FirstOrDefaultAsync(
                progress => progress.LearnerId == learnerId && progress.ResourceId == resourceId,
                cancellationToken);

        if (existing is null)
        {
            await context.ResourceProgress.AddAsync(
                new ResourceProgress
                {
                    LearnerId = learnerId,
                    ResourceId = resourceId,
                    PercentComplete = clamped,
                    LastOpenedAt = clock.Now,
                    UpdatedAt = clock.Now,
                },
                cancellationToken);

            return;
        }

        existing.PercentComplete = Math.Max(existing.PercentComplete, clamped);
        existing.LastOpenedAt = clock.Now;
        existing.UpdatedAt = clock.Now;
    }

    /// <summary>
    /// First and latest school-issued mark per subject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>By term, not by value.</strong> The tempting implementation is
    /// <c>MIN(mark)</c> and <c>MAX(mark)</c>, which looks equivalent and is not:
    /// a learner scoring 62, then 71, then 58 has a latest mark of 58 and has
    /// gone backwards by four, while min/max reports a latest of 71 and an
    /// improvement of plus thirteen.
    /// </para>
    /// <para>
    /// Worse, min/max can only ever subtract the smallest from the largest, so it
    /// reports a non-negative improvement for <em>every</em> learner — including
    /// one whose marks collapsed. This figure feeds the "most improved" badge and
    /// the subject-improvement number published to funders, so getting it wrong
    /// means telling a struggling child they are improving and telling a funder
    /// something untrue.
    /// </para>
    /// <para>
    /// The terms are ordered as strings, which is correct because the format is
    /// <c>2026-1</c> — zero-padded and year-first, so lexical order is
    /// chronological order.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<SubjectMarkTrend>> MarkTrendAsync(
        Guid learnerId,
        int schoolYear,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"{schoolYear}-";

        var marks = await context.AssessmentResults
            .AsNoTracking()
            .Where(result => result.LearnerId == learnerId
                && result.Source == MarkSource.SchoolReport
                && result.Term.StartsWith(prefix))
            .Select(result => new
            {
                result.SubjectId,
                SubjectName = result.Subject.Name,
                result.Term,
                result.Mark,
            })
            .ToListAsync(cancellationToken);

        return marks
            .GroupBy(mark => new { mark.SubjectId, mark.SubjectName })
            .Select(subject =>
            {
                var series = subject.OrderBy(mark => mark.Term, StringComparer.Ordinal).ToList();

                return new SubjectMarkTrend(
                    subject.Key.SubjectId,
                    subject.Key.SubjectName,
                    series[0].Mark,
                    series[^1].Mark,
                    series.Count);
            })
            .OrderByDescending(subject => subject.LatestMark)
            .ToList();
    }

    public async Task<IReadOnlyList<MarkPoint>> MarkHistoryAsync(
        Guid learnerId,
        int schoolYear,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"{schoolYear}-";

        return await context.AssessmentResults
            .AsNoTracking()
            .Where(result => result.LearnerId == learnerId && result.Term.StartsWith(prefix))
            .OrderBy(result => result.Term)
            .ThenBy(result => result.Subject.Name)
            .Select(result => new MarkPoint(
                result.Term,
                result.Subject.Name,
                result.Mark,
                result.Source))
            .ToListAsync(cancellationToken);
    }

    // ------------------------------------------------- IReadOnlyLearningProgress

    public Task<int> CompletedResourceCountAsync(
        Guid learnerId,
        CancellationToken cancellationToken = default) =>
        context.ResourceProgress
            .AsNoTracking()
            .CountAsync(
                progress => progress.LearnerId == learnerId && progress.PercentComplete >= 100,
                cancellationToken);

    /// <summary>
    /// The largest term-on-term improvement across school-issued marks.
    /// </summary>
    /// <remarks>
    /// Floored at zero, because this feeds a badge threshold and a negative
    /// "improvement" is not a smaller achievement — it is not an achievement.
    /// The <em>real</em> signed figure is on
    /// <see cref="SubjectMarkTrend.Improvement"/>, which is what the parent's
    /// screen shows, so the honest number is still visible where it matters.
    /// </remarks>
    public async Task<int> BestMarkImprovementAsync(
        Guid learnerId,
        int schoolYear,
        CancellationToken cancellationToken = default)
    {
        var trend = await MarkTrendAsync(learnerId, schoolYear, cancellationToken);

        return trend.Count == 0
            ? 0
            : Math.Max(0, (int)Math.Round(trend.Max(subject => subject.Improvement)));
    }

    /// <summary>Published resources only. A draft is invisible to everybody.</summary>
    private IQueryable<LearningResource> PublishedResources() =>
        context.Resources.AsNoTracking().Where(resource => resource.PublishedAt != null);

    /// <summary>
    /// The resource projection, closed over the learner whose progress to join.
    /// </summary>
    /// <remarks>
    /// An <c>Expression&lt;&gt;</c> factory rather than a method, so the
    /// projection is translated into SQL. Passing a method group here would
    /// silently make the query LINQ-to-Objects and read every resource in the
    /// hub into memory before projecting.
    /// </remarks>
    private static System.Linq.Expressions.Expression<Func<LearningResource, ResourceCard>> ProjectResource(
        Guid? learnerId) =>
        resource => new ResourceCard(
            resource.Id,
            resource.SubjectId,
            resource.Subject.Name,
            resource.Title,
            resource.Topic,
            resource.Type,
            resource.Grade,
            resource.SizeBytes,
            resource.DurationMinutes,
            learnerId == null
                ? 0
                : resource.Progress
                    .Where(progress => progress.LearnerId == learnerId)
                    .Select(progress => progress.PercentComplete)
                    .FirstOrDefault(),
            resource.PublishedAt);
}

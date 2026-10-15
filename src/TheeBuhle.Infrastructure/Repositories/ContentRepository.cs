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

/// <summary>Read models for the public website.</summary>
public sealed class ContentRepository(TheeBuhleDbContext context, IClock clock) : IContentRepository
{
    public async Task<IReadOnlyList<ProgrammeView>> ProgrammesAsync(
        CancellationToken cancellationToken = default) =>
        await ProgrammeQuery()
            .Where(programme => programme.IsActive)
            .OrderBy(programme => programme.SortOrder)
            .ThenBy(programme => programme.Number)
            .Select(Project)
            .ToListAsync(cancellationToken);

    public async Task<ProgrammeView?> ProgrammeAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        await ProgrammeQuery()
            .Where(programme => programme.Slug == slug && programme.IsActive)
            .Select(Project)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<VenueView>> VenuesAsync(CancellationToken cancellationToken = default) =>
        await context.Venues
            .AsNoTracking()
            .OrderBy(venue => venue.Name)
            .Select(venue => new VenueView(venue.Id, venue.Name, venue.Address, venue.Capacity))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Upcoming published events with a live registration count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count sums <c>PartySize</c> rather than counting rows. One booking for
    /// a family of five takes five places, and counting registrations instead of
    /// people would tell the foundation they had 115 places left on an event that
    /// was full — which is a bus with more children than seats.
    /// </para>
    /// <para>
    /// The now-comparison uses <see cref="IClock"/> rather than SQL's
    /// <c>GETDATE()</c>, so "upcoming" means upcoming in Johannesburg rather than
    /// in whatever timezone the database server is set to. On Azure SQL that is
    /// UTC, which would hide a 01:00 event for the two hours before it happened.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<EventView>> UpcomingEventsAsync(
        int limit,
        EventType? type = null,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        var events = context.Events
            .AsNoTracking()
            .Where(publicEvent => publicEvent.IsPublished && publicEvent.StartsAt >= now);

        if (type is { } wanted)
        {
            events = events.Where(publicEvent => publicEvent.Type == wanted);
        }

        return await events
            .OrderBy(publicEvent => publicEvent.StartsAt)
            .Take(limit)
            .Select(ProjectEvent)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventView>> PastEventsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        return await context.Events
            .AsNoTracking()
            .Where(publicEvent => publicEvent.IsPublished && publicEvent.StartsAt < now)
            .OrderByDescending(publicEvent => publicEvent.StartsAt)
            .Take(limit)
            .Select(ProjectEvent)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Finds a published event by id or slug.
    /// </summary>
    /// <remarks>
    /// Both, because a share link carries the slug and an admin link carries the
    /// id, and a donor forwarding either to a friend should not get a 404. The
    /// <c>IsPublished</c> filter applies to both paths — an unpublished event is
    /// invisible whichever way it is addressed, so a draft cannot be reached by
    /// guessing its id.
    /// </remarks>
    public async Task<EventView?> EventAsync(
        string idOrSlug,
        CancellationToken cancellationToken = default)
    {
        var asGuid = Guid.TryParse(idOrSlug, out var id) ? id : (Guid?)null;

        return await context.Events
            .AsNoTracking()
            .Where(publicEvent => publicEvent.IsPublished
                && (publicEvent.Slug == idOrSlug
                    || (asGuid != null && publicEvent.Id == asGuid)))
            .Select(ProjectEvent)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Published news posts.
    /// </summary>
    /// <remarks>
    /// "Published" means the date is set <em>and</em> is not in the future, so
    /// scheduling a post for next Monday genuinely hides it until then rather
    /// than relying on nobody guessing the URL.
    /// </remarks>
    public async Task<PagedResult<NewsSummary>> NewsAsync(
        NewsCategory? category,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        var posts = context.NewsPosts
            .AsNoTracking()
            .Where(post => post.PublishedAt != null && post.PublishedAt <= now);

        if (category is { } wanted)
        {
            posts = posts.Where(post => post.Category == wanted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            posts = posts.Where(post =>
                EF.Functions.Like(post.Title, $"%{term}%")
                || EF.Functions.Like(post.Excerpt, $"%{term}%"));
        }

        var total = await posts.CountAsync(cancellationToken);

        var items = await posts
            .OrderByDescending(post => post.IsFeatured)
            .ThenByDescending(post => post.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(post => new NewsSummary(
                post.Id,
                post.Slug,
                post.Category,
                post.Title,
                post.Excerpt,
                post.AuthorName,
                post.ReadMinutes,
                post.IsFeatured,
                post.PublishedAt!.Value))
            .ToListAsync(cancellationToken);

        return new PagedResult<NewsSummary>(items, total, page, pageSize);
    }

    public async Task<NewsArticle?> PostAsync(string slug, CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        return await context.NewsPosts
            .AsNoTracking()
            .Where(post => post.Slug == slug && post.PublishedAt != null && post.PublishedAt <= now)
            .Select(post => new NewsArticle(
                post.Id,
                post.Slug,
                post.Category,
                post.Title,
                post.Excerpt,
                post.BodyHtml,
                post.AuthorName,
                post.ReadMinutes,
                post.PublishedAt!.Value))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NewsSummary?> FeaturedPostAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        return await context.NewsPosts
            .AsNoTracking()
            .Where(post => post.IsFeatured && post.PublishedAt != null && post.PublishedAt <= now)
            .OrderByDescending(post => post.PublishedAt)
            .Select(post => new NewsSummary(
                post.Id,
                post.Slug,
                post.Category,
                post.Title,
                post.Excerpt,
                post.AuthorName,
                post.ReadMinutes,
                post.IsFeatured,
                post.PublishedAt!.Value))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The headline impact figures for one school year.
    /// </summary>
    /// <remarks>
    /// Read from the stored metrics that <c>ImpactService</c> refreshes, never
    /// aggregated live. The computation time comes back with them and is rendered
    /// on the page, so a visitor can see when the number was calculated rather
    /// than assuming it is live.
    /// </remarks>
    public async Task<IReadOnlyList<ImpactHeadline>> ImpactHeadlinesAsync(
        int schoolYear,
        CancellationToken cancellationToken = default) =>
        await context.ImpactMetrics
            .AsNoTracking()
            .Where(metric => metric.SchoolYear == schoolYear)
            .OrderBy(metric => metric.SortOrder)
            .Select(metric => new ImpactHeadline(
                metric.MetricKey,
                metric.Label,
                metric.ValueText,
                metric.Footnote,
                metric.ComputedAt))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Per-programme reach and attendance.
    /// </summary>
    /// <remarks>
    /// Computed live rather than stored, unlike the headline figures. These are
    /// cheaper — a count and an average per programme, over at most a handful of
    /// programmes — and they appear on one page rather than on every page load.
    /// </remarks>
    public async Task<IReadOnlyList<ProgrammeImpact>> ProgrammeImpactAsync(
        CancellationToken cancellationToken = default) =>
        await context.Programmes
            .AsNoTracking()
            .Where(programme => programme.IsActive)
            .OrderBy(programme => programme.SortOrder)
            .Select(programme => new ProgrammeImpact(
                programme.Id,
                programme.Name,
                programme.Enrolments
                    .Where(enrolment => enrolment.Status == EnrolmentStatus.Active)
                    .Select(enrolment => enrolment.LearnerId)
                    .Distinct()
                    .Count(),
                programme.Sessions.Count,
                programme.Sessions.SelectMany(session => session.Attendance).Any()
                    ? 100 * programme.Sessions
                        .SelectMany(session => session.Attendance)
                        .Count(record => record.Status == AttendanceStatus.Present
                            || record.Status == AttendanceStatus.Late)
                        / programme.Sessions.SelectMany(session => session.Attendance).Count()
                    : 0))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Average mark improvement by subject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Computed from school-issued marks only. Internal assessments are excluded
    /// because the foundation sets them — publishing improvement on your own
    /// tests is not evidence of anything, and a funder would be right to discount
    /// it.
    /// </para>
    /// <para>
    /// "First" and "latest" are by <em>term</em>, not by value. Taking the
    /// minimum and maximum mark instead can only ever produce a non-negative
    /// improvement, for every learner, including ones whose marks fell — so it
    /// would tell funders something untrue. Subjects with fewer than two terms of
    /// marks are excluded rather than reported as zero improvement.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<SubjectImprovement>> SubjectImprovementAsync(
        CancellationToken cancellationToken = default)
    {
        // Pulled back as per-learner-per-subject term series and reduced here.
        // A single SQL statement could do it with window functions, but it would
        // be a page of unreadable LINQ for a query that runs once per impact
        // page view over a few thousand rows.
        var marks = await context.AssessmentResults
            .AsNoTracking()
            .Where(result => result.Source == MarkSource.SchoolReport)
            .Select(result => new
            {
                result.SubjectId,
                SubjectName = result.Subject.Name,
                result.LearnerId,
                result.Term,
                result.Mark,
            })
            .ToListAsync(cancellationToken);

        // Grouped by subject NAME, not by subject id.
        //
        // A subject row is per grade — Mathematics Grade 9 and Mathematics
        // Grade 10 are different rows — so grouping by id put "Mathematics" on
        // the public impact page four separate times with four different
        // figures, which reads as a mistake rather than as detail.
        //
        // A visitor wants to know that maths marks improved by eleven points on
        // average. The per-grade breakdown is a staff question, and the staff
        // console can ask it from the same data.
        return marks
            .GroupBy(mark => mark.SubjectName, StringComparer.Ordinal)
            .Select(subject =>
            {
                var perLearner = subject
                    .GroupBy(mark => new { mark.SubjectId, mark.LearnerId })
                    .Select(learner => learner.OrderBy(mark => mark.Term, StringComparer.Ordinal).ToList())
                    // At least two terms, or there is no improvement to measure.
                    .Where(series => series.Count >= 2)
                    .Select(series => series[^1].Mark - series[0].Mark)
                    .ToList();

                return new SubjectImprovement(
                    // The id of any one of the grade-level rows, for a link.
                    // The name is what identifies the group.
                    subject.First().SubjectId,
                    subject.Key,
                    perLearner.Count,
                    perLearner.Count == 0 ? 0 : Math.Round(perLearner.Average(), 1));
            })
            .Where(subject => subject.LearnerCount > 0)
            .OrderByDescending(subject => subject.AverageImprovement)
            .ToList();
    }

    private IQueryable<Programme> ProgrammeQuery() => context.Programmes.AsNoTracking();

    // -------------------------------------------------------------------------
    //  The two projections, as Expression<> rather than as methods.
    //
    //  This is not a style choice. `.Select(SomeMethod)` passes a *delegate*,
    //  which LINQ cannot translate — so the query silently degrades to
    //  LINQ-to-Objects, loads every row in the table into memory and projects
    //  there. It produces the right answer, which is why it survives review, and
    //  it stops producing it quickly enough once there are a few thousand rows.
    //
    //  An Expression<> is translated into SQL, so only the projected columns are
    //  read and the filters run in the database where they belong.
    // -------------------------------------------------------------------------

    private static readonly System.Linq.Expressions.Expression<Func<Programme, ProgrammeView>> Project =
        programme => new ProgrammeView(
            programme.Id,
            programme.Slug,
            programme.Number,
            programme.Name,
            programme.Summary,
            programme.Description,
            programme.Expectations,
            programme.HowToJoin,
            programme.CtaLabel,
            programme.Audience,
            programme.CapacityNote,
            programme.MinGrade,
            programme.MaxGrade);

    private static readonly System.Linq.Expressions.Expression<Func<PublicEvent, EventView>> ProjectEvent =
        publicEvent => new EventView(
            publicEvent.Id,
            publicEvent.Title,
            publicEvent.Slug,
            publicEvent.Type,
            publicEvent.Venue != null ? publicEvent.Venue.Name : null,
            publicEvent.Location,
            publicEvent.StartsAt,
            publicEvent.EndsAt,
            publicEvent.Fee,
            publicEvent.FeeNote,
            publicEvent.Capacity,
            publicEvent.Description,
            // People, not bookings. One booking for a family of five takes five
            // places, and counting rows would report 115 places left on a bus
            // that is already full.
            publicEvent.Registrations.Sum(registration => registration.PartySize));
}

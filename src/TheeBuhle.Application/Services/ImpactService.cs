// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using System.Globalization;
using Microsoft.Extensions.Logging;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Application.Services;

/// <summary>
/// The public Impact page's numbers.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <em>not</em> computed on page load. The figures are aggregated
/// by <see cref="RefreshAsync"/> — run nightly and on demand by an administrator
/// — and written to the impact metrics table, which the page reads.
/// </para>
/// <para>Three reasons, in order of how much they matter:</para>
/// <list type="number">
/// <item><strong>Integrity.</strong> These are the numbers the foundation
/// publishes to donors and funders. A page that aggregates live can show a
/// half-captured Saturday register as a drop in attendance. Publishing a
/// computed snapshot means a figure on the website is a figure somebody can
/// stand behind.</item>
///
/// <item><strong>Honesty about provenance.</strong> The computation time is
/// stored and rendered, so a visitor can see when the number was calculated
/// rather than assuming it is live.</item>
///
/// <item><strong>Speed.</strong> Five aggregate queries across the attendance
/// table on every homepage load is the wrong use of a database.</item>
/// </list>
/// </remarks>
public sealed class ImpactService(
    IContentRepository content,
    IImpactRepository impact,
    IAttendanceRepository attendance,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<ImpactService> logger)
{
    /// <summary>
    /// Thin space between thousands, which is the South African convention —
    /// "1 240", not "1,240".
    /// </summary>
    private static readonly CultureInfo Formatting = CreateFormatting();

    /// <summary>Everything the Impact page and the homepage's impact band need.</summary>
    public async Task<ImpactSnapshot> PublicSnapshotAsync(
        int? schoolYear = null,
        CancellationToken cancellationToken = default)
    {
        var year = schoolYear ?? ReportingYear();

        return new ImpactSnapshot(
            year,
            await content.ImpactHeadlinesAsync(year, cancellationToken),
            await content.ProgrammeImpactAsync(cancellationToken),
            await content.SubjectImprovementAsync(cancellationToken));
    }

    /// <summary>Recomputes and republishes every headline metric.</summary>
    /// <returns>Metric key to published value.</returns>
    public async Task<IReadOnlyDictionary<string, string>> RefreshAsync(
        int? schoolYear = null,
        CancellationToken cancellationToken = default)
    {
        var year = schoolYear ?? ReportingYear();

        var learnersSupported = await impact.LearnersSupportedAsync(year, cancellationToken);
        var activeMentors = await impact.ActiveMentorCountAsync(year, cancellationToken);
        var tutoringHours = await impact.TutoringHoursAsync(year, cancellationToken);
        var mentorRetention = await impact.MentorRetentionAsync(year, cancellationToken);

        var statusTotals = await attendance.StatusTotalsAsync(cancellationToken);
        var totalMarked = statusTotals.Values.Sum();

        // Late still counts as attended. A learner who walks in at 09:20 because
        // the taxi was late was at the session, and an attendance figure that
        // says otherwise misrepresents both the child and the programme.
        var attended = statusTotals.GetValueOrDefault(AttendanceStatus.Present)
            + statusTotals.GetValueOrDefault(AttendanceStatus.Late);

        var overallRate = totalMarked == 0 ? 0 : (int)Math.Round(100.0 * attended / totalMarked);

        var computedAt = clock.Now;

        var metrics = new List<ImpactMetric>
        {
            new()
            {
                MetricKey = "learners_supported",
                SchoolYear = year,
                Label = "learners supported",
                ValueText = learnersSupported.ToString("N0", Formatting),
                Footnote = $"{year} school year",
                SortOrder = 1,
                ComputedAt = computedAt,
            },
            new()
            {
                MetricKey = "active_mentors",
                SchoolYear = year,
                Label = "active mentors",
                ValueText = activeMentors.ToString("N0", Formatting),
                Footnote = mentorRetention > 0
                    ? $"{mentorRetention}% completed the full year"
                    : null,
                SortOrder = 2,
                ComputedAt = computedAt,
            },
            new()
            {
                MetricKey = "attendance_rate",
                SchoolYear = year,
                Label = "overall attendance",
                ValueText = $"{overallRate}%",
                // The denominator is published alongside the percentage on
                // purpose. "94%" from twelve entries is not the same claim as
                // "94%" from four thousand, and a funder is entitled to tell
                // the difference.
                Footnote = $"across {totalMarked.ToString("N0", Formatting)} recorded session entries",
                SortOrder = 3,
                ComputedAt = computedAt,
            },
            new()
            {
                MetricKey = "tutoring_hours",
                SchoolYear = year,
                Label = "free tutoring hours delivered",
                ValueText = tutoringHours.ToString("N0", Formatting),
                Footnote = "attended sessions, by session length",
                SortOrder = 4,
                ComputedAt = computedAt,
            },
        };

        await unitOfWork.TransactionAsync(async () =>
        {
            foreach (var metric in metrics)
            {
                await impact.UpsertMetricAsync(metric, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        Log.ImpactRefreshed(logger, year, metrics.Count);

        return metrics.ToDictionary(metric => metric.MetricKey, metric => metric.ValueText);
    }

    /// <summary>
    /// The school year the figures describe.
    /// </summary>
    /// <remarks>
    /// The South African school year runs January to December, so before March
    /// the year worth reporting on is the one that just finished. In February the
    /// current year has one Saturday of data, and publishing it would show a
    /// catastrophic-looking drop in every figure on the page.
    /// </remarks>
    private int ReportingYear()
    {
        var now = clock.Now;

        return now.Month < 3 ? now.Year - 1 : now.Year;
    }

    private static CultureInfo CreateFormatting()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        // Escaped rather than typed literally: an invisible non-breaking space
        // in source is the kind of character a reformat silently turns back into
        // an ordinary one.
        culture.NumberFormat.NumberGroupSeparator = " ";
        culture.NumberFormat.NumberDecimalDigits = 0;

        return CultureInfo.ReadOnly(culture);
    }
}

public sealed record ImpactSnapshot(
    int SchoolYear,
    IReadOnlyList<ImpactHeadline> Headlines,
    IReadOnlyList<ProgrammeImpact> Programmes,
    IReadOnlyList<SubjectImprovement> Subjects);

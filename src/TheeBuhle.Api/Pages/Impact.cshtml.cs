// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// Impact &amp; Statistics — web screen 06.
/// </summary>
/// <remarks>
/// <para>
/// Every figure on this page is computed from the attendance registers and
/// school-issued results, republished nightly, and stamped with when it was
/// computed. The page says so.
/// </para>
/// <para>
/// It also says where the foundation fell short, which is the difference
/// between an impact report and a brochure — and the reason the subject bars
/// can show a negative number rather than being clamped at zero.
/// </para>
/// </remarks>
public sealed class ImpactModel(
    IContentRepository content,
    ImpactService impact) : SitePageModel(content)
{
    protected override string PageKey => "impact";

    public ImpactSnapshot Snapshot { get; private set; } = new(0, [], [], []);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PreparePageAsync(
            "Impact & Statistics",
            "Attendance, mark improvement and tutoring hours delivered — computed from our own "
            + "registers and school-issued results, with the gaps published alongside.",
            cancellationToken);

        Snapshot = await impact.PublicSnapshotAsync(null, cancellationToken);
    }

    /// <summary>
    /// The width of a subject's bar, as a percentage of the longest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scaled against the largest improvement so the longest bar fills the track
    /// and the rest are readable in proportion — a fixed 0-to-100 scale would
    /// render every bar as a stub, because an eleven-point improvement is a
    /// large real result and a small fraction of a hundred.
    /// </para>
    /// <para>
    /// A <em>negative</em> improvement gets the minimum width rather than a
    /// negative one. The number beside it still reads "−2 points", so the bar is
    /// a visual aid and the figure is the truth — which is the right way round
    /// when a subject has gone backwards.
    /// </para>
    /// </remarks>
    public int BarWidthFor(SubjectImprovement subject)
    {
        var largest = Snapshot.Subjects
            .Select(s => s.AverageImprovement)
            .DefaultIfEmpty(1m)
            .Max();

        if (largest <= 0 || subject.AverageImprovement <= 0)
        {
            return 6;
        }

        return Math.Max(6, (int)Math.Round(100 * subject.AverageImprovement / largest));
    }
}

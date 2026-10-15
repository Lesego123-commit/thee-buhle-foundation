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
/// Home / About Us — web screen 01 of the client-approved prototype.
/// </summary>
/// <remarks>
/// <para>
/// Section order follows the approved frame exactly: hero, values, founder,
/// programmes, impact, events, donations, news.
/// </para>
/// <para>
/// Everything on this page is loaded server-side and rendered into the HTML.
/// A client-rendered homepage on a slow Vaal mobile connection shows a blank
/// page and then a jump, and a crawler or a WhatsApp preview sees nothing at
/// all — which for an NPO that nobody is searching for by name yet is most of
/// what makes the site findable.
/// </para>
/// </remarks>
public sealed class IndexModel(
    IContentRepository content,
    ImpactService impact) : SitePageModel(content)
{
    protected override string PageKey => "home";

    public IReadOnlyList<ProgrammeView> Programmes { get; private set; } = [];

    public ImpactSnapshot Impact { get; private set; } = new(0, [], [], []);

    public IReadOnlyList<EventView> Events { get; private set; } = [];

    public IReadOnlyList<NewsSummary> News { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PreparePageAsync(
            "About Us",
            "Thee Buhle Foundation walks alongside young people in the Vaal — tutoring, "
            + "mentorship and life skills that outlast a single school year.",
            cancellationToken);

        Programmes = await SiteContent.ProgrammesAsync(cancellationToken);
        Impact = await impact.PublicSnapshotAsync(null, cancellationToken);
        Events = await SiteContent.UpcomingEventsAsync(3, null, cancellationToken);

        var news = await SiteContent.NewsAsync(null, null, 1, 3, cancellationToken);

        News = news.Items;
    }

    /// <summary>
    /// One published metric by key.
    /// </summary>
    /// <remarks>
    /// By key rather than by position, so the markup does not silently show the
    /// wrong figure if the sort order of the metrics ever changes.
    /// </remarks>
    public ImpactHeadline? Metric(string key) =>
        Impact.Headlines.FirstOrDefault(headline => headline.MetricKey == key);

    /// <summary>
    /// A gradient per programme card.
    /// </summary>
    /// <remarks>
    /// The client has not supplied photography, so the cards are distinguished
    /// by colour rather than by an image. Four gradients drawn from the Heritage
    /// palette, cycled — which looks deliberate, where four grey placeholder
    /// boxes would look unfinished.
    /// </remarks>
    public static string GradientFor(int index) => (index % 4) switch
    {
        0 => "linear-gradient(150deg,#2E4034,#5C6B4F)",
        1 => "linear-gradient(150deg,#8B7A55,#C2A870)",
        2 => "linear-gradient(150deg,#6B4F3A,#A87C32)",
        _ => "linear-gradient(150deg,#3A4A55,#7E93A0)",
    };
}

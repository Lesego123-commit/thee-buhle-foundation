// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// Events — web screen 05.
/// </summary>
/// <remarks>
/// The places-left figure is computed from the registrations rather than typed,
/// so the number on the page is the number in the database. A full event shows
/// a closed state instead of a form that will be refused — which matters more
/// than it sounds, because the capacity check happens inside a locking
/// transaction and would otherwise reject somebody who had already filled in
/// their details.
/// </remarks>
public sealed class EventsModel(IContentRepository content) : SitePageModel(content)
{
    protected override string PageKey => "events";

    public IReadOnlyList<EventView> Upcoming { get; private set; } = [];

    public IReadOnlyList<EventView> Past { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PreparePageAsync(
            "Events",
            "Workshops, fundraisers, open days and community activities in the Vaal. "
            + "Most are free; where there is a fee it goes straight into programme costs.",
            cancellationToken);

        Upcoming = await SiteContent.UpcomingEventsAsync(24, null, cancellationToken);
        Past = await SiteContent.PastEventsAsync(6, cancellationToken);
    }

    /// <summary>The event types, for the filter chips.</summary>
    public static (EventType Value, string Label, string Chip)[] Types { get; } =
    [
        (EventType.Fundraising, "Fundraising", "chip--gold"),
        (EventType.Workshop, "Workshop", "chip"),
        (EventType.OpenDay, "Open day", "chip--mist"),
        (EventType.Community, "Community", "chip--mist"),
    ];

    public static string LabelFor(EventType type) =>
        Types.FirstOrDefault(t => t.Value == type).Label ?? type.ToString();

    public static string ChipFor(EventType type) =>
        Types.FirstOrDefault(t => t.Value == type).Chip ?? "chip";

    /// <summary>
    /// The fee, as the page says it.
    /// </summary>
    /// <remarks>
    /// The staff-written note if there is one, because "R80 per entry, R200 for
    /// a family of four" is more useful than "R80.00" — and "Free" rather than
    /// "R0.00", which reads as a missing value.
    /// </remarks>
    public static string FeeFor(EventView occasion) =>
        !string.IsNullOrWhiteSpace(occasion.FeeNote)
            ? occasion.FeeNote
            : occasion.Fee > 0
                ? $"{SiteFormat.Rands(occasion.Fee)} entry"
                : "Free";
}

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using Microsoft.AspNetCore.Mvc.RazorPages;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// The base every public page model derives from.
/// </summary>
/// <remarks>
/// <para>
/// Carries the two things every page needs and no page should have to remember:
/// the metadata the layout renders into <c>&lt;title&gt;</c> and the Open Graph
/// tags, and the next upcoming event for the announcement bar.
/// </para>
/// <para>
/// The announcement bar is fetched here rather than in each page because it
/// appears on all of them — and rendered server-side rather than by JavaScript
/// because a bar that is empty for a second and then appears pushes the whole
/// page down as somebody is reaching for a link.
/// </para>
/// </remarks>
public abstract class SitePageModel : PageModel
{
    /// <summary>
    /// The content repository, for derived pages to use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named <c>SiteContent</c> rather than <c>Content</c> because
    /// <see cref="PageModel"/> already has a <c>Content(string)</c> method for
    /// returning a content result — and a property that hides an inherited
    /// method is a name collision waiting to confuse somebody.
    /// </para>
    /// <para>
    /// Exposed here rather than each page taking its own copy, so a derived page
    /// does not hold a second reference to the same object.
    /// </para>
    /// </remarks>
    protected IContentRepository SiteContent { get; }

    /// <summary>
    /// An explicit constructor rather than a primary one.
    /// </summary>
    /// <remarks>
    /// A primary constructor parameter that is both captured by the base and
    /// assigned to a property produces CS9124 — the value would be held twice.
    /// An ordinary constructor assigns it once.
    /// </remarks>
    protected SitePageModel(IContentRepository content) => SiteContent = content;

    /// <summary>Which navigation item to mark as current.</summary>
    protected abstract string PageKey { get; }

    /// <summary>
    /// Sets the page metadata and loads the announcement bar.
    /// </summary>
    /// <remarks>
    /// Called by each page's handler rather than from a filter, so a page that
    /// needs different metadata per item — an article, a programme — can pass
    /// its own after loading the content it is about.
    /// </remarks>
    protected async Task PreparePageAsync(
        string title,
        string description,
        CancellationToken cancellationToken)
    {
        ViewData["Page"] = PageKey;
        ViewData["Title"] = title;
        ViewData["Description"] = description;

        var upcoming = await SiteContent.UpcomingEventsAsync(1, null, cancellationToken);

        if (upcoming.Count > 0)
        {
            var next = upcoming[0];

            ViewData["NextEvent"] = new NextEventBanner(
                next.Title,
                next.Slug,
                // "Sat 18 October", which is what somebody needs to decide
                // whether they are free — not a full date with a year.
                next.StartsAt.ToString("ddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-ZA")),
                next.VenueName ?? next.Location ?? "Vanderbijlpark");
        }
    }
}

/// <summary>The next event, as the announcement bar shows it.</summary>
public sealed record NextEventBanner(string Title, string Slug, string When, string Where);

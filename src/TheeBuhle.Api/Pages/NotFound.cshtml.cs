// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// The 404 page.
/// </summary>
/// <remarks>
/// <para>
/// Offers the four things a lost visitor is most likely to be looking for
/// rather than a dead end. A donor who followed an old link out of a newsletter
/// should not have to go back to the home page and start again.
/// </para>
/// <para>
/// Reached two ways, and it keeps the visitor's own URL in both: the
/// status-code-pages branch in <c>Program.cs</c> re-executes any non-API 404
/// through this page, and a page that cannot find its own record — an article,
/// a programme — returns <c>NotFound()</c> and lands here as well. The status
/// stays 404 either way, so a crawler is told the truth about the link it
/// followed.
/// </para>
/// </remarks>
public sealed class NotFoundModel(IContentRepository content) : SitePageModel(content)
{
    /// <summary>No navigation item is current: the visitor is not on any of them.</summary>
    protected override string PageKey => string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await PreparePageAsync(
            "Page not found",
            "That page does not exist. Here is where most people are heading.",
            cancellationToken);

    /// <summary>Where lost visitors are usually trying to get to.</summary>
    public static (string Heading, string Body, string Href)[] Destinations { get; } =
    [
        ("Our programmes", "Tutoring, mentorship, workshops and holiday camps.", "/programmes"),
        ("Donate", "Give once, give monthly, or sponsor a learner.", "/donate"),
        ("Volunteer", "Two hours a week and one skill.", "/volunteer"),
        ("Talk to us", "We answer every message.", "/contact"),
    ];
}

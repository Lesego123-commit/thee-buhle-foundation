// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// The staff console — the screen the client's own people work in.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a <see cref="SitePageModel"/>: the console has its own
/// chrome and must not carry the public site's navigation, announcement bar or
/// footer. It also loads no data at all. Every figure on it is fetched by the
/// browser from the same REST API the mobile app uses, with the signed-in
/// staff member's own access token.
/// </para>
/// <para>
/// There is no server-side authorisation on this page, and that is correct
/// rather than an omission: the page is an empty shell until somebody signs in,
/// and the token that authorises the data lives in JavaScript, not in a cookie
/// the browser would attach to this navigation. The authorisation that matters
/// happens on every API call the console makes, which is where the
/// authorisation tests assert it — a staff member who loads this page and is
/// not the safeguarding lead is refused the concerns endpoint with a 403,
/// whatever the page chooses to show them.
/// </para>
/// </remarks>
public sealed class AdminModel : PageModel
{
    public void OnGet()
    {
        // Nothing to prepare. Kept explicit rather than omitted, so the absence
        // of a data load is a decision somebody made rather than a gap.
    }
}

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
/// Redeeming a code from the foundation: accepting an invitation, or finishing
/// a password reset. The same thing the app's "I have a code" screen does.
/// </summary>
/// <remarks>
/// There is no sign-up form anywhere on the site, by design: staff create
/// accounts, because only the foundation can say who is whose parent.
/// </remarks>
public sealed class ActivateModel(IContentRepository content) : SitePageModel(content)
{
    protected override string PageKey => string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await PreparePageAsync(
            "Use your code",
            "Set your password with the one-time code the foundation gave you.",
            cancellationToken);
}

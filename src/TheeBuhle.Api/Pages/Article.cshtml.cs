// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// A single news article — web screen 08.
/// </summary>
/// <remarks>
/// A missing article returns 404 rather than redirecting to the news index.
/// A redirect would tell a crawler, and anybody who followed a shared link,
/// that the article still exists somewhere — and it would hide a broken link
/// from us instead of showing it.
/// </remarks>
public sealed class ArticleModel(IContentRepository content) : SitePageModel(content)
{
    /// <summary>Marks News as the current section, since this page lives inside it.</summary>
    protected override string PageKey => "news";

    public NewsArticle Post { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        var post = await SiteContent.PostAsync(slug, cancellationToken);

        if (post is null)
        {
            return NotFound();
        }

        Post = post;

        // The article's own title and excerpt, so a link shared on WhatsApp
        // previews the story rather than the site's name.
        await PreparePageAsync(post.Title, post.Excerpt, cancellationToken);

        return Page();
    }
}

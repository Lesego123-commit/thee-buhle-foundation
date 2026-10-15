// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// News and blog index — web screen 07.
/// </summary>
/// <remarks>
/// <para>
/// Filtered and paginated server-side, unlike the events page. The two look
/// similar and are deliberately different: there are four upcoming events and
/// there will be four hundred articles, so a crawler and a visitor with
/// JavaScript switched off both need to be able to reach page four of the
/// success stories by following a link.
/// </para>
/// <para>
/// That is also why the category lives in the query string rather than in
/// script state — a filtered list is a page somebody can send to somebody else.
/// </para>
/// </remarks>
public sealed class NewsModel(IContentRepository content) : SitePageModel(content)
{
    /// <summary>Articles per page, matching the approved prototype.</summary>
    private const int PerPage = 6;

    protected override string PageKey => "news";

    public IReadOnlyList<NewsSummary> Posts { get; private set; } = [];

    /// <summary>
    /// The featured article, on the unfiltered first page only.
    /// </summary>
    /// <remarks>
    /// Repeating the featured banner above every page of results would push the
    /// actual list down the screen on each click, and showing it above a
    /// filtered list would show an article that does not belong to the filter.
    /// </remarks>
    public NewsSummary? Featured { get; private set; }

    public int Total { get; private set; }

    public int PageNumber { get; private set; } = 1;

    public int PageCount { get; private set; } = 1;

    /// <summary>The active category slug, or null for "all posts".</summary>
    public string? CategorySlug { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? category,
        int page,
        CancellationToken cancellationToken)
    {
        NewsCategory? filter = null;

        if (!string.IsNullOrWhiteSpace(category))
        {
            var match = Categories.FirstOrDefault(
                candidate => string.Equals(candidate.Slug, category, StringComparison.OrdinalIgnoreCase));

            // An unrecognised slug is redirected rather than quietly ignored.
            // Serving every article under a URL that says ?category=sport
            // would tell the reader they are looking at a filtered list when
            // they are not.
            if (match.Slug is null)
            {
                return Redirect("/news");
            }

            filter = match.Value;
            CategorySlug = match.Slug;
        }

        PageNumber = Math.Max(1, page);

        await PreparePageAsync(
            "News & Blog",
            "Programme updates, success stories, community initiatives and press coverage — "
            + "written by the people doing the work.",
            cancellationToken);

        var results = await SiteContent.NewsAsync(filter, null, PageNumber, PerPage, cancellationToken);

        Posts = results.Items;
        Total = results.Total;
        PageCount = Math.Max(1, results.Pages);

        if (PageNumber == 1 && filter is null)
        {
            Featured = await SiteContent.FeaturedPostAsync(cancellationToken);
        }

        return Page();
    }

    /// <summary>The categories, for the filter chips.</summary>
    /// <remarks>
    /// The slugs are the ones the PHP prototype published, so links the client
    /// has already shared keep working.
    /// </remarks>
    public static (string Slug, NewsCategory Value, string Label)[] Categories { get; } =
    [
        ("success_story", NewsCategory.SuccessStory, "Success stories"),
        ("programme_update", NewsCategory.ProgrammeUpdate, "Programme updates"),
        ("community", NewsCategory.Community, "Community"),
        ("announcement", NewsCategory.Announcement, "Announcements"),
        ("press", NewsCategory.Press, "Press"),
    ];

    public static string LabelFor(NewsCategory category) =>
        Categories.FirstOrDefault(candidate => candidate.Value == category).Label
        ?? category.ToString();

    /// <summary>
    /// The slug for a category, or null if it has none.
    /// </summary>
    /// <remarks>
    /// Null rather than a thrown exception, so adding a category to the enum
    /// and forgetting to add it here loses a crumb link rather than turning
    /// every article of that category into a 500.
    /// </remarks>
    public static string? SlugFor(NewsCategory category) =>
        Categories.FirstOrDefault(candidate => candidate.Value == category).Slug;

    /// <summary>
    /// A link to another page of results that keeps the current filter.
    /// </summary>
    /// <remarks>
    /// Named <c>LinkFor</c> rather than <c>PageUrl</c> because a member whose
    /// name ends in "Url" is expected by the analysers to return a
    /// <see cref="Uri"/>, and a relative path is not one.
    /// </remarks>
    public string LinkFor(int target)
    {
        var number = target.ToString(CultureInfo.InvariantCulture);

        return CategorySlug is null
            ? $"/news?page={number}"
            : $"/news?page={number}&category={Uri.EscapeDataString(CategorySlug)}";
    }
}

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
/// A single programme, for a direct link, a search result or a printed
/// information sheet.
/// </summary>
/// <remarks>
/// The programmes index already carries all four in full, so this page exists
/// for the cases the index cannot serve: a link in a WhatsApp group that should
/// open on the mentorship programme rather than at the top of a long page, and
/// a sheet a parent can print and take to a school.
/// </remarks>
public sealed class ProgrammeModel(IContentRepository content) : SitePageModel(content)
{
    protected override string PageKey => "programmes";

    public ProgrammeView Programme { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        var programme = await SiteContent.ProgrammeAsync(slug, cancellationToken);

        if (programme is null)
        {
            return NotFound();
        }

        Programme = programme;

        await PreparePageAsync(programme.Name, programme.Summary, cancellationToken);

        return Page();
    }
}

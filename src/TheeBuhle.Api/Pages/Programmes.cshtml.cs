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
/// Programmes — web screen 02.
/// </summary>
/// <remarks>
/// Every word on this page — the copy, the expectations, the joining
/// instructions — comes from the programmes table, so staff can change what the
/// site says about a programme from the console without a deployment. The
/// template decides only the alternating layout.
/// </remarks>
public sealed class ProgrammesModel(IContentRepository content) : SitePageModel(content)
{
    protected override string PageKey => "programmes";

    public IReadOnlyList<ProgrammeView> Programmes { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PreparePageAsync(
            "Programmes",
            "Tutoring, mentorship, life-skills workshops and holiday camps for young people "
            + "in the Vaal — all free to learners.",
            cancellationToken);

        Programmes = await SiteContent.ProgrammesAsync(cancellationToken);
    }
}

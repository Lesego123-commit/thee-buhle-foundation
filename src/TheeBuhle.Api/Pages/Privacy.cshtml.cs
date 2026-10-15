// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
// =============================================================================

using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// The privacy policy.
/// </summary>
/// <remarks>
/// <para>
/// Written to describe what the system actually does rather than to cover the
/// organisation legally with generalities. Every claim on the page corresponds
/// to something in the schema or the code, which is the only kind of privacy
/// policy worth publishing:
/// </para>
/// <list type="bullet">
/// <item>"only the last four digits" — <c>volunteer_applications.id_number_last4</c></item>
/// <item>"no card details" — the <c>donations</c> table has no payment columns</item>
/// <item>"a reference code, not a name" — <c>learners.reference_code</c></item>
/// <item>"consent history is kept" — <c>consents</c> is append-only</item>
/// <item>"every access is logged" — <c>audit_log</c>, with no update or delete path</item>
/// </list>
/// <para>
/// Which means this page is also a test: if one of those claims stops being
/// true, the sentence here becomes false and has to be changed with it.
/// </para>
/// </remarks>
public sealed class PrivacyModel(IContentRepository content) : SitePageModel(content)
{
    protected override string PageKey => "privacy";

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await PreparePageAsync(
            "Privacy policy",
            "How Thee Buhle Foundation collects, uses and protects personal information, "
            + "in line with POPIA.",
            cancellationToken);
}

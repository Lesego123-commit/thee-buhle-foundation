// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// Child safeguarding and POPIA.
/// </summary>
/// <remarks>
/// <para>
/// This page exists because the system enforces a safeguarding policy, and a
/// policy nobody can read is not a policy. Everything stated on it corresponds
/// to something in the code:
/// </para>
/// <list type="bullet">
/// <item>screening before assignment — <c>MentorshipService.AssertClearedAsync</c></item>
/// <item>concerns reach the lead only — <c>AccessPolicy.CanReadFlaggedConcerns</c></item>
/// <item>concerns never stop quietly — the escalation chain's terminal handler</item>
/// <item>no private learner messaging — no such endpoint exists in the API</item>
/// <item>mentor-approved pairing — the peer repository has no direct-approve path</item>
/// </list>
/// <para>
/// The claim that a concern "cannot silently stop" is the one worth checking
/// first: the integration suite asserts a 503 and an unchanged check-in count
/// when nobody can be routed to, which is what makes the sentence on this page
/// true rather than aspirational.
/// </para>
/// </remarks>
public sealed class SafeguardingModel(IContentRepository content) : SitePageModel(content)
{
    protected override string PageKey => "safeguarding";

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await PreparePageAsync(
            "POPIA & child safeguarding",
            "How we protect the children in our programmes, and how to report a concern.",
            cancellationToken);
}

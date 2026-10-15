// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.Globalization;
using TheeBuhle.Application.Listeners;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// Donations — web screen 03.
/// </summary>
/// <remarks>
/// <para>
/// The form on this page collects a name, an email address, an amount and a
/// frequency. It does not collect a card number, an expiry date or a CVV, and
/// there is no field for one anywhere in the markup — card details are entered
/// on the payment provider's own site. That is the single most important
/// property of this page, and it is visible in the HTML rather than only
/// asserted in a document.
/// </para>
/// <para>
/// The received-so-far figure comes from completed donations only. A pending
/// gateway redirect is money nobody has sent yet, and publishing it as income
/// would be the kind of overstatement an NPO cannot afford to make by accident.
/// </para>
/// </remarks>
public sealed class DonateModel(
    IContentRepository content,
    ILearnerRepository learners,
    IDonationRepository donations,
    Section18AOptions registration) : SitePageModel(content)
{
    protected override string PageKey => "donate";

    public IReadOnlyList<SponsorCard> SponsorCards { get; private set; } = [];

    public DonationTotals Totals { get; private set; } = new(0, 0, 0, 0);

    /// <summary>
    /// The learner a visitor arrived wanting to sponsor, if any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The "sponsor this learner" buttons are links to <c>?sponsor=A-214</c>
    /// rather than inline click handlers. The prototype used
    /// <c>onclick="..."</c>, which this build's content-security policy blocks
    /// outright — <c>script-src 'self'</c> with no <c>'unsafe-inline'</c> — so
    /// those buttons would have looked fine and done nothing.
    /// </para>
    /// <para>
    /// Doing it in the query string is also better than a handler would have
    /// been: the form arrives already set to that learner with no script at
    /// all, and the link can be sent to somebody.
    /// </para>
    /// </remarks>
    public string? SponsorReference { get; private set; }

    public async Task OnGetAsync(string? sponsor, CancellationToken cancellationToken)
    {
        await PreparePageAsync(
            "Donate",
            "Give once, give monthly, or walk a whole year with one learner. Every rand is "
            + "receipted and every allocation is published.",
            cancellationToken);

        SponsorCards = await learners.SponsorableCardsAsync(3, cancellationToken);
        Totals = await donations.TotalsAsync(cancellationToken);

        // Only a reference that is actually sponsorable is honoured. Anything
        // else is ignored rather than pre-selected, because a form that opens
        // holding a learner who already has a sponsor would be refused on
        // submit after the donor had filled everything in.
        if (!string.IsNullOrWhiteSpace(sponsor)
            && SponsorCards.Any(card =>
                string.Equals(card.ReferenceCode, sponsor, StringComparison.OrdinalIgnoreCase)))
        {
            SponsorReference = sponsor;
        }
    }

    /// <summary>
    /// The audited allocation of the 2025 financial year.
    /// </summary>
    /// <remarks>
    /// Held here as data rather than repeated in the markup, and stated in rand
    /// as well as percent — a percentage on its own is a claim that cannot be
    /// checked against a financial statement.
    /// </remarks>
    public static (string Label, decimal Amount, int Percent, string Colour)[] Allocation { get; } =
    [
        ("Programme delivery — tutors, mentors, materials", 1_284_000m, 82, "var(--forest)"),
        ("Transport & meals for learners", 188_000m, 12, "var(--gold)"),
        ("Administration, audit & compliance", 94_000m, 6, "var(--ink40)"),
    ];

    /// <summary>
    /// What a single gift buys, in the client's own words.
    /// </summary>
    /// <remarks>
    /// Amounts rather than pre-written strings, so every figure on the page is
    /// spaced by <see cref="SiteFormat.Rands"/> and none of them can wrap
    /// across a line while its neighbour does not.
    /// </remarks>
    public static (decimal Amount, string Buys)[] GiftLadder { get; } =
    [
        (120m, "a term of stationery and a workbook for one learner"),
        (250m, "one learner’s Saturday tutoring for a month"),
        (600m, "a full term of weekly mentorship"),
        (1_800m, "sponsors one learner for a whole school year"),
        (9_500m, "runs one four-day holiday camp for 30 children"),
    ];

    /// <summary>The donor questions, for the accordion.</summary>
    /// <remarks>
    /// The tax question is asked only when it can be answered truthfully: with
    /// the foundation's real PBO number from configuration. Until that is set,
    /// the question is left out rather than answered with a placeholder a donor
    /// might rely on at tax time.
    /// </remarks>
    public IReadOnlyList<(string Question, string Answer)> Questions =>
        registration.CanIssueCertificates
            ?
            [
                ("Is my donation tax deductible?",
                    $"Yes. We are a registered PBO ({registration.PboDisplay}) and issue a Section 18A certificate "
                    + $"by email for any donation over R{registration.Threshold.ToString("0", CultureInfo.InvariantCulture)}."),
                .. GeneralQuestions,
            ]
            : GeneralQuestions;

    private static readonly (string Question, string Answer)[] GeneralQuestions =
    [
        ("Can I cancel a monthly donation?",
            "Any time, from the link in every receipt email or by replying to us. No notice period, "
            + "no questions."),
        ("How do I know the money reaches learners?",
            "Our annual financials are published in full and independently audited. Every programme "
            + "cost line is broken down above, and the attendance and results figures on our Impact "
            + "page are computed from our own registers."),
        ("Can my company donate goods instead of money?",
            "Yes — stationery, laptops, data and venue space are all useful. Use the partnerships "
            + "form on our Contact page."),
        ("Do you store my card details?",
            "No. Card details are entered on our payment provider’s site and never reach this "
            + "website. All we keep is your name, your email address and the payment reference."),
    ];

    /// <summary>A gradient per sponsorship card, in place of the photograph we may not publish.</summary>
    public static string GradientFor(int index) => (index % 3) switch
    {
        0 => "linear-gradient(150deg,#2E4034,#6B7A5E)",
        1 => "linear-gradient(150deg,#8B7A55,#C2A870)",
        _ => "linear-gradient(150deg,#3A4A55,#7E93A0)",
    };
}

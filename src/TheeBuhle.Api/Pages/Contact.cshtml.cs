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
/// Contact and partnerships — web screen 09.
/// </summary>
/// <remarks>
/// <para>
/// The one thing on this page that is not cosmetic: choosing "Report a concern"
/// routes the message to the designated safeguarding lead's mailbox and never
/// to the general enquiries inbox. That routing happens in the API, not here —
/// but the page says so, because somebody reporting a concern about a child
/// needs to know where it is going before they type it.
/// </para>
/// <para>
/// The topic can be preselected from the query string, so "Register a learner"
/// on a programme page lands here with the right option already chosen.
/// </para>
/// </remarks>
public sealed class ContactModel(IContentRepository content) : SitePageModel(content)
{
    protected override string PageKey => "contact";

    /// <summary>The topic the visitor arrived on, defaulting to a general enquiry.</summary>
    public string SelectedTopic { get; private set; } = "general";

    public async Task OnGetAsync(string? topic, CancellationToken cancellationToken)
    {
        // Only a topic that exists is honoured. An unknown one falls back to
        // "general" rather than leaving every chip unpressed, which would post
        // a message with no topic at all.
        if (!string.IsNullOrWhiteSpace(topic)
            && Topics.Any(candidate => string.Equals(candidate.Value, topic, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedTopic = topic.ToLowerInvariant();
        }

        await PreparePageAsync(
            "Talk to us",
            "General enquiries, media requests, or a conversation about partnering with us. "
            + "We answer every message, usually within two working days.",
            cancellationToken);
    }

    /// <summary>
    /// The topics, in the order the approved prototype shows them.
    /// </summary>
    /// <remarks>
    /// The values are what the API validates against, so they live here rather
    /// than in the markup — a chip whose value is misspelled would send a
    /// safeguarding report to the general inbox.
    /// </remarks>
    public static (string Value, string Label)[] Topics { get; } =
    [
        ("general", "General enquiry"),
        ("enrol", "Enrol my child"),
        ("partnership", "Partnership or sponsorship"),
        ("media", "Media & press"),
        ("volunteering", "Volunteering"),
        ("concern", "Report a concern"),
    ];

    /// <summary>The four ways organisations work with us.</summary>
    public static (string Number, string Heading, string Body, string Note)[] Partnerships { get; } =
    [
        ("01", "Corporate giving",
            "Fund a programme line for a year — tutoring, transport, meals or camps. From R25 000.",
            "Section 18A + B-BBEE SED certificate"),
        ("02", "Skills-based partnership",
            "Send your staff as tutors or mentors. We handle screening, training and scheduling.",
            "Minimum 6 volunteers, 1 term"),
        ("03", "Schools & universities",
            "Referral pathways, shared venues, and student volunteers earning service-learning credits.",
            "MOU-based, no cost"),
        ("04", "In-kind & goods",
            "Stationery, laptops, data, printing, venue space, catering for camps and events.",
            "Receipted at fair value"),
    ];

    /// <summary>The supporters the client has asked us to credit.</summary>
    public static string[] Supporters { get; } =
    [
        "RUN STUDIO", "YD FITNESS", "FIKA CAFÉ", "VAAL WEEKLY", "SEBOKENG HIGH", "VAAL LOGISTICS",
    ];
}

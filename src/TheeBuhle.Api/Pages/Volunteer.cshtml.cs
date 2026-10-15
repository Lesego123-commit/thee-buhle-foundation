// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// Volunteer sign-up — web screen 04.
/// </summary>
/// <remarks>
/// <para>
/// The compliance block on this page is not decoration. South African law
/// requires anyone working with children to be screened against the National
/// Child Protection Register, so the form says so, asks for consent to it
/// explicitly, and sets out what happens next.
/// </para>
/// <para>
/// Note the hint on the identity-number field. The full number is needed for
/// the SAPS clearance application and the API keeps only the last four digits.
/// Saying that at the point of asking is the difference between collecting the
/// data and collecting it honestly — and it is the same sentence the POPIA
/// section of the privacy policy makes.
/// </para>
/// </remarks>
public sealed class VolunteerModel(IContentRepository content) : SitePageModel(content)
{
    protected override string PageKey => "volunteer";

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await PreparePageAsync(
            "Volunteer with us",
            "Bring one skill and two hours a week. All volunteers working with children complete "
            + "a police clearance and our child-safeguarding induction.",
            cancellationToken);

    /// <summary>
    /// The skills a volunteer can offer.
    /// </summary>
    /// <remarks>
    /// Sent to the API as the strings shown here, which is why they are held in
    /// one list rather than typed into the markup: a chip whose label drifts
    /// from the value would file an application under a skill nobody screens
    /// for.
    /// </remarks>
    public static string[] Skills { get; } =
    [
        "Maths", "Physical Science", "English", "Accounting", "Life Orientation",
        "Mentoring", "CV & interview coaching", "Public speaking", "Sport & recreation",
        "Admin & data capture", "Driving / transport", "Photography & social media",
        "Fundraising", "IT support",
    ];

    public static string[] Days { get; } = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public static string[] Slots { get; } = ["Morning", "Afternoon", "Evening"];

    /// <summary>What the screening requires, and who does it.</summary>
    public static (string Document, string Note)[] Documents { get; } =
    [
        ("Copy of ID or passport", "Bring the original to your induction"),
        ("Police clearance (SAPS)", "We apply on your behalf — no action needed"),
        ("Two contactable references", "Names and numbers, at your induction"),
        ("Signed safeguarding code", "Signed at your induction"),
    ];

    /// <summary>The steps after the form, so nobody is left waiting without knowing why.</summary>
    public static (string Step, string When)[] NextSteps { get; } =
    [
        ("We read your application", "Within 3 working days"),
        ("A short phone call", "15 minutes, to match you well"),
        ("Screening & induction", "SAPS clearance + 2-hour induction"),
        ("Your first session", "You are paired with a staff lead"),
    ];

    /// <summary>The roles the programme team is currently short of.</summary>
    public static (string Role, string Needed)[] MostNeeded { get; } =
    [
        ("Maths tutors, Grade 10–12", "6 needed"),
        ("Mentors", "12 needed"),
        ("Saturday transport drivers", "3 needed"),
        ("Camp facilitators (Dec)", "8 needed"),
    ];
}

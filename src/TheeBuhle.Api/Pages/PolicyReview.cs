// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.Globalization;

namespace TheeBuhle.Api.Pages;

/// <summary>
/// When each published policy was last reviewed.
/// </summary>
/// <remarks>
/// <para>
/// Fixed dates, deliberately. The prototype printed the current month, so the
/// privacy policy claimed to have been reviewed this month on every single day
/// of its life — which is a statement about governance that nobody had actually
/// made. A policy page that dates itself is asserting that somebody read it,
/// and only a person can assert that.
/// </para>
/// <para>
/// So these change when the policy is reviewed, in the same commit as the
/// wording, and they are separate values because the two policies are reviewed
/// by different people at different times.
/// </para>
/// </remarks>
public static class PolicyReview
{
    public static DateOnly Privacy { get; } = new(2026, 9, 1);

    public static DateOnly Safeguarding { get; } = new(2026, 9, 1);

    /// <summary>"September 2026" — the month, because the day is noise on a policy.</summary>
    public static string Describe(DateOnly reviewed) =>
        reviewed.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-ZA"));
}

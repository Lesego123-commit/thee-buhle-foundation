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
/// The formatting the public pages share.
/// </summary>
/// <remarks>
/// <para>
/// One place, because money written two ways on two pages is how a figure
/// starts looking like two different figures.
/// </para>
/// <para>
/// The thousands separator is a space rather than a comma, which is the South
/// African convention and the same one <c>site.js</c> applies to the amounts it
/// rewrites in the browser. It is specifically a non-breaking space, so
/// "R1 800" cannot be split across two lines and read as two amounts.
/// </para>
/// </remarks>
public static class SiteFormat
{
    /// <summary>"R1 800", or "R1 800.50" with cents.</summary>
    public static string Rands(decimal amount, bool withCents = false) =>
        "R" + amount
            .ToString(withCents ? "#,##0.00" : "#,##0", CultureInfo.InvariantCulture)
            .Replace(",", " ", StringComparison.Ordinal);

    /// <summary>
    /// A phone number as written for people ("+27 16 000 0000") turned into a
    /// <c>tel:</c> target ("+27160000000"): digits and a leading plus only.
    /// </summary>
    public static string TelHref(string displayed)
    {
        ArgumentNullException.ThrowIfNull(displayed);

        var digits = new string(displayed.Where(char.IsAsciiDigit).ToArray());

        return displayed.TrimStart().StartsWith('+') ? "+" + digits : digits;
    }
}

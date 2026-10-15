// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Infrastructure.Support;

/// <summary>
/// The system clock, pinned to South African time.
/// </summary>
/// <remarks>
/// <para>
/// Pinned rather than local, and pinned rather than UTC. Azure App Service runs
/// in UTC and a developer's machine runs in whatever they have set, so "today"
/// would be a different day depending on where the code happened to execute —
/// which matters here more than it sounds: <c>ILearnerRepository</c> allocates
/// reference codes per day, a permission form closes on a date, and a session
/// that starts at 09:00 on a Saturday is stored against that Saturday.
/// </para>
/// <para>
/// South Africa has no daylight saving, so SAST is a fixed UTC+02:00 all year.
/// That is unusually convenient: the conversion cannot produce an ambiguous or
/// non-existent local time, which is the failure mode that makes pinned
/// timezones risky elsewhere.
/// </para>
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <summary>
    /// Looked up by IANA id, with the Windows id as a fallback.
    /// </summary>
    /// <remarks>
    /// .NET on Linux knows "Africa/Johannesburg" and .NET on Windows knows
    /// "South Africa Standard Time". Both are tried because this code runs on
    /// Windows in development and Linux on Azure, and a missing timezone throws
    /// at startup rather than returning a wrong answer.
    /// </remarks>
    private static readonly TimeZoneInfo SouthAfrica = ResolveTimeZone();

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SouthAfrica);

    public DateOnly Today => DateOnly.FromDateTime(Now.Date);

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Africa/Johannesburg", "South Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the other spelling.
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupt registry or tzdata entry; try the other spelling.
            }
        }

        // Last resort rather than a crash. SAST has no daylight saving, so a
        // fixed offset is behaviourally identical — it just cannot describe
        // itself by name.
        return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "South Africa", "SAST");
    }
}

/// <summary>
/// Allow-list HTML sanitisation, for the two fields stored as markup.
/// </summary>
/// <remarks>
/// <para>
/// An <strong>allow-list</strong>, not a block-list, and the distinction is the
/// whole security property. A block-list has to anticipate every attack —
/// <c>&lt;script&gt;</c>, then <c>onerror=</c>, then <c>javascript:</c>, then
/// <c>&lt;svg onload=</c>, then whatever is published next year — and is one
/// unanticipated vector from being useless. An allow-list permits eleven tags
/// and drops everything else, including the vectors nobody has thought of.
/// </para>
/// <para>
/// Applied on write. The database therefore cannot contain a payload at all, so
/// a future template that forgets to escape, or a second client that renders the
/// field differently, is not a new vulnerability. Task 1 section 9.3.13.5.
/// </para>
/// </remarks>
public sealed class HtmlSanitiserAdapter : IHtmlSanitiser
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly HtmlSanitizer sanitizer = Build();

    public string Sanitise(string dirtyHtml) =>
        string.IsNullOrWhiteSpace(dirtyHtml) ? string.Empty : sanitizer.Sanitize(dirtyHtml);

    /// <summary>
    /// A plain-text rendering, for a push notification or SMS preview.
    /// </summary>
    /// <remarks>
    /// Sanitises first and then strips, rather than stripping directly. Stripping
    /// tags from unsanitised input with a regex is the classic way to turn
    /// <c>&lt;scr&lt;script&gt;ipt&gt;</c> into a working payload — the strip
    /// removes the inner tag and leaves a valid outer one. Going through the
    /// sanitiser first means the input is already a known-good tree.
    /// </remarks>
    public string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var clean = sanitizer.Sanitize(html);

        // Block boundaries become spaces rather than vanishing, so two
        // paragraphs do not run into one word.
        var spaced = Regex.Replace(
            clean,
            "</(p|div|li|h[1-6]|blockquote)>",
            " ",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        var stripped = Regex.Replace(spaced, "<[^>]+>", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1));

        return Whitespace.Replace(WebUtility.HtmlDecode(stripped), " ").Trim();
    }

    private static HtmlSanitizer Build()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedCssProperties.Clear();

        // Eleven tags, chosen from what staff actually write in an announcement:
        // paragraphs, emphasis, a list, a heading, a link, a line break. No
        // images (an announcement is text, and an image tag carries onerror), no
        // tables, no iframes, no style attribute.
        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "ul", "ol", "li", "a" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        // href only, on anchors only. No target, no rel, no title — and
        // certainly no event handlers, which is what an empty attribute
        // allow-list guarantees rather than hopes for.
        sanitizer.AllowedAttributes.Add("href");

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        // Not http. A link in an announcement goes to the foundation's own site
        // or to a government page, both of which are https — and allowing plain
        // http would let a notice downgrade a parent's connection.
        sanitizer.KeepChildNodes = true;

        return sanitizer;
    }
}

/// <summary>
/// A request context for work that has no HTTP request: a scheduled job, a
/// migration, a seeder.
/// </summary>
/// <remarks>
/// Exists so <see cref="IRequestContext"/> has an implementation outside the
/// web layer. The audit trail records "system" rather than a fabricated IP
/// address, so a row written by the nightly job is distinguishable from one
/// written by a person — which it would not be if the job borrowed a plausible
/// address.
/// </remarks>
public sealed class SystemRequestContext : IRequestContext
{
    public string IpAddress => "0.0.0.0";

    public string UserAgent => "system";
}

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Microsoft (2026) ASP.NET Core documentation, including WebApplicationFactory integration testing.
// =============================================================================

using System.Net;

namespace TheeBuhle.IntegrationTests;

/// <summary>
/// The staff console, and the script contract the whole front end depends on.
/// </summary>
/// <remarks>
/// <para>
/// These exist because of a defect that every other kind of test missed. The
/// pages rendered, the API answered, the suite was green — and not one form on
/// the website worked, because the layout set the API's base URL in an inline
/// <c>&lt;script&gt;</c> that this application's own content-security policy
/// blocks. The assignment never ran, the client fell back to the PHP
/// prototype's <c>/api/v1</c>, and every submission 404'd.
/// </para>
/// <para>
/// So the assertions below are about the markup rather than about behaviour:
/// no inline script anywhere the policy would refuse it, and the base URL
/// carried by an attribute the browser will actually honour.
/// </para>
/// </remarks>
public sealed class ConsoleTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task The_console_renders_its_sign_in_screen()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/admin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Staff console", html, StringComparison.Ordinal);
        Assert.Contains("id=\"signinForm\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The console ships no data of its own.
    /// </summary>
    /// <remarks>
    /// The page is a shell: every figure on it is fetched by the browser with
    /// the signed-in staff member's token. If learner data ever appears in this
    /// response, it is being served to anybody who types the URL — no sign-in,
    /// no role check, no audit entry.
    /// </remarks>
    [Fact]
    public async Task The_console_page_contains_no_learner_data()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/admin", UriKind.Relative));

        var html = await response.Content.ReadAsStringAsync();

        // Seeded learners, their reference codes and their guardians. None of
        // them has any business being in an unauthenticated response.
        foreach (var leak in new[] { "Mahlangu", "A-214", "Sebokeng Secondary", "@example.co.za" })
        {
            Assert.DoesNotContain(leak, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_console_refuses_to_be_indexed()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/admin", UriKind.Relative));

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("noindex", html, StringComparison.Ordinal);
        Assert.Contains("name=\"referrer\" content=\"no-referrer\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// No page carries an inline script, because the policy this API sends
    /// would refuse to run it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check is deliberately crude — any <c>&lt;script&gt;</c> with a body
    /// and no <c>src</c> — because the failure it guards against is silent.
    /// A blocked inline script does not raise an error the server ever sees; it
    /// simply does not happen, and whatever depended on it quietly uses a
    /// default.
    /// </para>
    /// <para>
    /// JSON-LD is exempt. It is a data block the browser never executes, so the
    /// policy has nothing to refuse — and it is what makes the site legible to
    /// a search engine.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/")]
    [InlineData("/donate")]
    [InlineData("/volunteer")]
    [InlineData("/contact")]
    [InlineData("/events")]
    [InlineData("/admin")]
    public async Task A_page_carries_no_inline_script(string route)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri(route, UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        var offending = System.Text.RegularExpressions.Regex
            .Matches(html, "<script(?<attrs>[^>]*)>(?<body>.*?)</script>",
                System.Text.RegularExpressions.RegexOptions.Singleline)
            .Where(match => !match.Groups["attrs"].Value.Contains("src=", StringComparison.Ordinal))
            // "application/ld" rather than the full type: the tag helper
            // re-renders the attribute with the plus encoded as &#x2B;, which
            // the browser decodes back but a literal comparison does not.
            .Where(match => !match.Groups["attrs"].Value.Contains("application/ld", StringComparison.Ordinal))
            .Where(match => !string.IsNullOrWhiteSpace(match.Groups["body"].Value))
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"{route} carries {offending.Count} inline script(s) that the site's own CSP blocks: "
            + string.Join(" | ", offending.Select(match => match.Groups["body"].Value.Trim())));
    }

    /// <summary>
    /// The API base is carried by an attribute, and points at a path this
    /// application actually serves.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/admin")]
    public async Task A_page_points_the_client_at_the_real_api(string route)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri(route, UriKind.Relative));

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-api-base=\"/api\"", html, StringComparison.Ordinal);

        // The global this replaced. Set from an inline script, it never ran.
        Assert.DoesNotContain("TB_API_BASE =", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The client's own default base is a path this API serves.
    /// </summary>
    /// <remarks>
    /// The whole defect came down to a fallback: when the configured value did
    /// not arrive, the client used one from a different codebase. This asserts
    /// the file's default rather than the page's attribute, so the two cannot
    /// drift apart again.
    /// </remarks>
    [Fact]
    public async Task The_api_client_defaults_to_a_path_this_api_serves()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/assets/js/api.js", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var script = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("|| '/api/v1'", script, StringComparison.Ordinal);
        Assert.Contains("|| '/api'", script, StringComparison.Ordinal);
    }
}

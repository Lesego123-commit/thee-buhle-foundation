// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Microsoft (2026) ASP.NET Core documentation, including WebApplicationFactory integration testing.
// =============================================================================

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheeBuhle.IntegrationTests;

/// <summary>
/// The public website, over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// These are deliberately shallow — a status code, a content type, one string
/// from the page — because the point is not to test the wording. It is that
/// every page still renders at all: a Razor page's model binds at request time,
/// so a page that references a property which no longer exists compiles and
/// then throws on the first visitor.
/// </para>
/// <para>
/// The 404 assertions are here because of a real defect. The not-found page was
/// once wired so that a mistyped URL answered 404 with an <em>empty body</em>:
/// the status was right, the page was blank, and a test that checked only the
/// status code passed. Every assertion below checks the body as well.
/// </para>
/// </remarks>
public sealed class WebsiteTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    /// <summary>
    /// Every page of the site renders.
    /// </summary>
    /// <remarks>
    /// The whole route list, driven from data, so adding a page to the site is
    /// one line here rather than a new test.
    /// </remarks>
    [Theory]
    [InlineData("/")]
    [InlineData("/programmes")]
    [InlineData("/impact")]
    [InlineData("/events")]
    [InlineData("/news")]
    [InlineData("/donate")]
    [InlineData("/volunteer")]
    [InlineData("/contact")]
    [InlineData("/privacy")]
    [InlineData("/safeguarding")]
    [InlineData("/not-found")]
    public async Task A_page_renders(string route)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri(route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();

        // The layout rendered, so the page has navigation and a footer rather
        // than being a fragment, or an error body with a 200 on it.
        Assert.Contains("Thee Buhle", html, StringComparison.Ordinal);
        Assert.Contains("</html>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page whose record does not exist answers 404 with the site's own page.
    /// </summary>
    [Theory]
    [InlineData("/news/no-such-article-exists")]
    [InlineData("/programmes/no-such-programme-exists")]
    [InlineData("/an/old/link/from/a/newsletter")]
    public async Task A_missing_page_answers_404_with_the_not_found_page(string route)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri(route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // Both halves matter: the status tells a crawler the link is dead, and
        // the body gives the person who followed it somewhere to go.
        Assert.Contains("We could not find that page", html, StringComparison.Ordinal);
        Assert.Contains("/programmes", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unknown API route stays JSON even though the website re-executes 404s.
    /// </summary>
    /// <remarks>
    /// The status-code branch that renders the not-found page is scoped away
    /// from <c>/api</c>. Without this test, widening that scope by one line
    /// would hand every mobile client an HTML error page to parse.
    /// </remarks>
    [Fact]
    public async Task An_unknown_api_route_answers_json()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/no-such-endpoint", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An article page renders the seeded article.
    /// </summary>
    /// <remarks>
    /// The slug is read from the API rather than typed in, so this test follows
    /// the seed data instead of pinning it.
    /// </remarks>
    [Fact]
    public async Task An_article_page_renders_its_article()
    {
        using var client = fixture.CreateClient();

        var listing = await client.GetFromJsonAsync<NewsPageRow>(
            new Uri("/api/public/news?page=1&pageSize=1", UriKind.Relative));

        Assert.NotNull(listing);
        Assert.NotEmpty(listing.Items);

        var post = listing.Items[0];

        using var response = await client.GetAsync(new Uri($"/news/{post.Slug}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(post.Title, html, StringComparison.Ordinal);
        Assert.Contains(post.AuthorName, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A programme page renders the seeded programme.
    /// </summary>
    [Fact]
    public async Task A_programme_page_renders_its_programme()
    {
        using var client = fixture.CreateClient();

        var programmes = await client.GetFromJsonAsync<List<ProgrammeRow>>(
            new Uri("/api/public/programmes", UriKind.Relative));

        Assert.NotNull(programmes);
        Assert.NotEmpty(programmes);

        var programme = programmes[0];

        using var response = await client.GetAsync(
            new Uri($"/programmes/{programme.Slug}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(WebUtility.HtmlEncode(programme.Name), html, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unrecognised news category redirects rather than serving everything.
    /// </summary>
    /// <remarks>
    /// Serving the full list under a URL that says <c>?category=sport</c> would
    /// tell the reader they were looking at a filtered list when they were not.
    /// </remarks>
    [Fact]
    public async Task An_unknown_news_category_redirects_to_the_index()
    {
        using var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync(new Uri("/news?category=sport", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/news", response.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// The donation form collects no card details, and cannot quietly start to.
    /// </summary>
    /// <remarks>
    /// The strongest claim the privacy policy makes about this site is that a
    /// card number never reaches it. That claim is only as good as the markup,
    /// so the markup is asserted: no field, hint or label anywhere on the page
    /// that has to do with a card.
    /// </remarks>
    [Theory]
    [InlineData("cvv")]
    [InlineData("cvc")]
    [InlineData("expiry")]
    [InlineData("cc-number")]
    [InlineData("card_number")]
    [InlineData("cardnumber")]
    public async Task The_donation_page_has_no_card_field(string forbidden)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/donate", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A sponsorship link arrives with that learner already chosen.
    /// </summary>
    /// <remarks>
    /// The prototype did this with an inline <c>onclick</c>, which the content
    /// security policy this API sends blocks outright — so the button looked
    /// right and did nothing. This asserts the query-string version that
    /// replaced it, because the failure it replaced was invisible.
    /// </remarks>
    [Fact]
    public async Task A_sponsor_link_preselects_that_learner()
    {
        using var client = fixture.CreateClient();

        var cards = await client.GetFromJsonAsync<List<SponsorRow>>(
            new Uri("/api/public/sponsorships/available?limit=1", UriKind.Relative));

        Assert.NotNull(cards);
        Assert.NotEmpty(cards);

        var reference = cards[0].ReferenceCode;

        using var response = await client.GetAsync(
            new Uri($"/donate?sponsor={Uri.EscapeDataString(reference)}", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains($"value=\"{reference}\" selected", html, StringComparison.Ordinal);
        Assert.Contains("data-freq=\"sponsor\" aria-pressed=\"true\"", html, StringComparison.Ordinal);
    }

    private sealed record NewsPageRow(List<NewsRow> Items);

    private sealed record NewsRow(string Slug, string Title, string AuthorName);

    private sealed record ProgrammeRow(string Slug, string Name);

    private sealed record SponsorRow(string ReferenceCode);
}

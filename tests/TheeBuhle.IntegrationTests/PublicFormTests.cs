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

namespace TheeBuhle.IntegrationTests;

/// <summary>
/// What the public forms accept, refuse, and say while refusing.
/// </summary>
/// <remarks>
/// Two things are asserted here that no other test covers. First, that consent
/// is a rule on every public write rather than a checkbox the website happens
/// to draw — a donation and an event registration used to be accepted without
/// it, because neither the request nor the table had anywhere to put the
/// answer. Second, that a refusal is written for the person reading it: the
/// framework's own wording ("The field Body must be a string or array type with
/// a minimum length of '10'") is not something to show a parent.
/// </remarks>
public sealed class PublicFormTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task A_donation_without_consent_is_refused()
    {
        using var client = fixture.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/public/donations", UriKind.Relative),
            new
            {
                displayName = "No Consent",
                email = "no.consent@example.co.za",
                amount = 250m,
                frequency = "Once",
                consentPopia = false,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("consent", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_donation_with_consent_is_accepted()
    {
        using var client = fixture.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/public/donations", UriKind.Relative),
            new
            {
                displayName = "With Consent",
                email = "with.consent@example.co.za",
                amount = 250m,
                frequency = "Once",
                consentPopia = true,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_event_registration_without_consent_is_refused()
    {
        using var client = fixture.CreateClient();

        var events = await client.GetFromJsonAsync<List<EventRow>>(
            new Uri("/api/public/events", UriKind.Relative));

        Assert.NotNull(events);
        Assert.NotEmpty(events);

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/public/events/{events[0].Slug}/register", UriKind.Relative),
            new
            {
                attendeeName = "No Consent",
                email = "no.consent.event@example.co.za",
                partySize = 1,
                consentPopia = false,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("consent", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A validation refusal is written for the person who filled in the form.
    /// </summary>
    /// <remarks>
    /// The assertion is on what the message does NOT say. Pinning the exact
    /// wording would make every copy change a failing test; pinning the absence
    /// of the framework's vocabulary — "the field", "must be a string or array
    /// type" — catches the case that matters, which is an attribute somebody
    /// adds later without a message of its own.
    /// </remarks>
    [Fact]
    public async Task A_validation_message_is_written_for_a_person()
    {
        using var client = fixture.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/public/contact", UriKind.Relative),
            new
            {
                topic = "General",
                name = "Too Short",
                email = "not-an-email",
                body = "short",
                consentPopia = true,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemRow>();

        Assert.NotNull(problem);
        Assert.NotNull(problem.Errors);

        var messages = problem.Errors.Values.SelectMany(each => each).ToList();

        Assert.NotEmpty(messages);

        foreach (var message in messages)
        {
            Assert.DoesNotContain("must be a string or array type", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("The field ", message, StringComparison.Ordinal);
            Assert.DoesNotContain("minimum length of", message, StringComparison.OrdinalIgnoreCase);

            // Addressed to somebody, and a sentence rather than a field dump.
            Assert.EndsWith(".", message.Trim(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every message the public forms can produce reads like a sentence.
    /// </summary>
    /// <remarks>
    /// Driven from the routes rather than one test each, because the failure
    /// this guards against is a new attribute on one DTO that nobody gave a
    /// message to.
    /// </remarks>
    [Theory]
    [InlineData("/api/public/newsletter")]
    [InlineData("/api/public/volunteer")]
    [InlineData("/api/public/donations")]
    public async Task A_public_form_never_answers_with_framework_wording(string route)
    {
        using var client = fixture.CreateClient();

        // Deliberately empty: every required field fails at once, which is the
        // widest sample of messages a single call can produce.
        using var response = await client.PostAsJsonAsync(new Uri(route, UriKind.Relative), new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemRow>();

        Assert.NotNull(problem?.Errors);

        foreach (var message in problem.Errors.Values.SelectMany(each => each))
        {
            Assert.DoesNotContain("The field ", message, StringComparison.Ordinal);
            Assert.DoesNotContain("is required", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record EventRow(string Slug);

    private sealed record ValidationProblemRow(Dictionary<string, List<string>> Errors);
}

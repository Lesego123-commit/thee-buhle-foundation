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
/// The authorisation boundary, over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests assert that <c>AccessPolicy</c> answers correctly. These
/// assert that the <em>API</em> does — which is a different claim, because a
/// route with a missing attribute or a controller that forgets to consult the
/// policy would pass every unit test.
/// </para>
/// <para>
/// Written as the matrix, because the interesting failures are the cells
/// somebody forgets rather than the happy paths.
/// </para>
/// </remarks>
public sealed class AuthorisationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // ------------------------------------------------------- anonymous access

    /// <summary>
    /// Every protected route refuses an anonymous caller.
    /// </summary>
    /// <remarks>
    /// Driven from a list rather than one test each, so adding a route to the
    /// list is the whole cost of covering it — and a route that is protected in
    /// one place and open in another shows up as one failure.
    /// </remarks>
    [Theory]
    [InlineData("/api/dashboard")]
    [InlineData("/api/mentor/dashboard")]
    [InlineData("/api/messages")]
    [InlineData("/api/notifications")]
    [InlineData("/api/safeguarding/concerns")]
    [InlineData("/api/safeguarding/escalation-chain")]
    [InlineData("/api/admin/learners")]
    [InlineData("/api/admin/audit")]
    [InlineData("/api/admin/volunteers")]
    [InlineData("/api/admin/donations")]
    [InlineData("/api/peers/suggestions")]
    [InlineData("/api/auth/me")]
    public async Task A_protected_route_refuses_an_anonymous_caller(string route)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri(route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The public routes are genuinely public.
    /// </summary>
    /// <remarks>
    /// The control case. Without it, a change that accidentally required
    /// authentication everywhere would pass every test above — and the website
    /// would simply stop working for visitors, who are the people the
    /// foundation most wants to reach.
    /// </remarks>
    [Theory]
    [InlineData("/api/public/programmes")]
    [InlineData("/api/public/events")]
    [InlineData("/api/public/news")]
    [InlineData("/api/public/impact")]
    [InlineData("/api/public/sponsorships/available")]
    [InlineData("/api/public/donations/impact-copy")]
    [InlineData("/health")]
    [InlineData("/health/live")]
    public async Task A_public_route_answers_an_anonymous_caller(string route)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri(route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ----------------------------------------------------- the learner record

    /// <summary>
    /// A parent cannot read another family's child.
    /// </summary>
    /// <remarks>
    /// The single most important assertion in the system, asserted here through
    /// the real API against the real guardian-link table. Both parents are
    /// genuine, signed-in users with children on the programme; the only thing
    /// separating them is a row in one table.
    /// </remarks>
    [Fact]
    public async Task A_parent_cannot_read_another_familys_child()
    {
        var learnerId = await fixture.LearnerIdAsync("A-214");

        // A-214's guardian is Nomvula Mahlangu. Joseph Nkosi is A-227's.
        var stranger = await fixture.SignInAsync("joseph.nkosi@example.co.za");

        using var response = await stranger.GetAsync(
            new Uri($"/api/learners/{learnerId}/progress", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// The refusal does not distinguish a real learner from an invented one.
    /// </summary>
    /// <remarks>
    /// Otherwise the endpoint becomes an oracle for discovering which learner
    /// ids exist — one request at a time, from any parent account. The status and
    /// the message have to be identical.
    /// </remarks>
    [Fact]
    public async Task A_refusal_looks_the_same_for_a_real_learner_and_an_invented_one()
    {
        var real = await fixture.LearnerIdAsync("A-214");
        var invented = Guid.CreateVersion7();

        var stranger = await fixture.SignInAsync("joseph.nkosi@example.co.za");

        using var forReal = await stranger.GetAsync(
            new Uri($"/api/learners/{real}/progress", UriKind.Relative));

        using var forInvented = await stranger.GetAsync(
            new Uri($"/api/learners/{invented}/progress", UriKind.Relative));

        Assert.Equal(forReal.StatusCode, forInvented.StatusCode);

        var realBody = await forReal.Content.ReadAsStringAsync();
        var inventedBody = await forInvented.Content.ReadAsStringAsync();

        // The correlation id differs per request, so the detail is compared
        // rather than the whole body.
        Assert.Equal(DetailOf(realBody), DetailOf(inventedBody));
    }

    [Fact]
    public async Task A_parent_can_read_their_own_child()
    {
        var learnerId = await fixture.LearnerIdAsync("A-214");

        var parent = await fixture.SignInAsync("nomvula.mahlangu@example.co.za");

        using var response = await parent.GetAsync(
            new Uri($"/api/learners/{learnerId}/progress", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------ role boundaries

    /// <summary>
    /// The safeguarding queue is closed to ordinary programme staff.
    /// </summary>
    /// <remarks>
    /// The point of the flag: a concern may be <em>about</em> a member of
    /// programme staff, so a queue visible to all staff would be meaningless in
    /// exactly the cases it exists for.
    /// </remarks>
    [Fact]
    public async Task Programme_staff_cannot_reach_the_safeguarding_queue()
    {
        var staff = await fixture.SignInAsync("programmes@theebuhlefoundation.co.za");

        using var response = await staff.GetAsync(new Uri("/api/safeguarding/concerns", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_safeguarding_lead_can_reach_the_safeguarding_queue()
    {
        var lead = await fixture.SignInAsync("safeguarding@theebuhlefoundation.co.za");

        using var response = await lead.GetAsync(new Uri("/api/safeguarding/concerns", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The audit log is closed to ordinary staff and open to the lead.
    /// </summary>
    /// <remarks>
    /// This is the pair that caught a real inconsistency: <c>AccessPolicy</c>
    /// permitted the safeguarding lead while the route carried
    /// <c>[RequireAdmin]</c> and refused them. The route was corrected to match
    /// the policy, and this asserts the agreed answer at the boundary so the two
    /// cannot drift again.
    /// </remarks>
    [Fact]
    public async Task The_audit_log_is_closed_to_staff_and_open_to_the_lead()
    {
        var staff = await fixture.SignInAsync("programmes@theebuhlefoundation.co.za");
        var lead = await fixture.SignInAsync("safeguarding@theebuhlefoundation.co.za");

        using var refused = await staff.GetAsync(new Uri("/api/admin/audit", UriKind.Relative));
        using var allowed = await lead.GetAsync(new Uri("/api/admin/audit", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task A_parent_cannot_reach_the_staff_console()
    {
        var parent = await fixture.SignInAsync("nomvula.mahlangu@example.co.za");

        using var response = await parent.GetAsync(new Uri("/api/admin/learners", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_mentor_cannot_reach_the_parent_dashboard()
    {
        var mentor = await fixture.SignInAsync("thandi.mokoena@example.co.za");

        using var response = await mentor.GetAsync(new Uri("/api/dashboard", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ------------------------------------------------------- mass assignment

    /// <summary>
    /// A client cannot promote itself by sending extra fields.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The classic mass-assignment attack: post a field the request model does
    /// not declare and hope the server binds it anyway. Here the sign-in request
    /// is sent with <c>role: "Admin"</c> attached.
    /// </para>
    /// <para>
    /// The defence is structural rather than a filter — the request records
    /// declare exactly the fields they accept, and the role is resolved from the
    /// database, never from the request. This asserts that rather than trusting
    /// it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Sending_an_extra_role_field_does_not_promote_the_caller()
    {
        using var client = fixture.CreateClient();

        // Its own address, for the same reason the sign-in helper gives each
        // account one: the limiter is per-address, and this test signs in
        // separately from the cached helper.
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.200.0.1");

        using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new
        {
            email = "nomvula.mahlangu@example.co.za",
            password = "Vanderbijlpark2026!",
            returnRefreshTokenInBody = true,

            // Not on the request model. Ignored, or the whole authorisation
            // system is decorative.
            role = "Admin",
            isAdmin = true,
            staffId = Guid.CreateVersion7(),
        });

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SignInRow>();

        Assert.NotNull(payload);
        Assert.Equal("Parent", payload.User.Role);
        Assert.Null(payload.User.StaffId);
    }

    // --------------------------------------------------------- injection

    /// <summary>
    /// SQL in a query parameter is inert.
    /// </summary>
    /// <remarks>
    /// Parameterised by EF Core throughout, so this is a regression guard rather
    /// than a discovery — but it is the guard that would catch somebody
    /// reaching for raw SQL string concatenation later. The assertion is that the
    /// search simply finds nothing, which is what a literal string should do.
    /// </remarks>
    [Theory]
    [InlineData("'; DROP TABLE learners; --")]
    [InlineData("' OR '1'='1")]
    [InlineData("%' UNION SELECT NULL, password_hash FROM users --")]
    public async Task Sql_in_a_search_parameter_is_treated_as_text(string payload)
    {
        var staff = await fixture.SignInAsync("programmes@theebuhlefoundation.co.za");

        using var response = await staff.GetAsync(
            new Uri($"/api/admin/learners?query={Uri.EscapeDataString(payload)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // No learner is named "'; DROP TABLE", so the search finds nothing.
        Assert.Contains("\"total\":0", body.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        // And the table is still there.
        using var sanity = await staff.GetAsync(new Uri("/api/admin/learners", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, sanity.StatusCode);
    }

    private static string DetailOf(string body)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);

        return document.RootElement.TryGetProperty("detail", out var detail)
            ? detail.GetString() ?? string.Empty
            : body;
    }

    private sealed record SignInRow(string AccessToken, CurrentUserRow User);

    private sealed record CurrentUserRow(Guid UserId, string Email, string Role, Guid? StaffId);
}

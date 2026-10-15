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
using System.Text.Json;

namespace TheeBuhle.IntegrationTests;

/// <summary>
/// Adding people and letting them in: invitations, resets, and who may do what.
/// </summary>
/// <remarks>
/// Against the real database, because the rules that matter here are spread
/// across the service, the unique indexes and the sign-in path. Each test uses
/// its own email addresses and its own client address, so they do not share
/// rate-limit budgets or collide with each other's accounts.
/// </remarks>
public sealed class PeopleTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Manager = "programmes@theebuhlefoundation.co.za";
    private const string Admin = "admin@theebuhlefoundation.co.za";
    private const string ParentAccount = "nomvula.mahlangu@example.co.za";
    private const string NewPassword = "Saturday tutoring at nine";

    private static string UniqueEmail(string who) => $"{who}.{Guid.NewGuid():N}@example.co.za";

    /// <summary>An anonymous client with an address of its own, so tests do not throttle each other.</summary>
    private HttpClient Anonymous()
    {
        var client = fixture.CreateClient();
        var bytes = Guid.NewGuid().ToByteArray();

        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{bytes[0]}.{bytes[1]}.{bytes[2]}");

        return client;
    }

    private async Task<(Guid UserId, string Code)> AddParentAsync(HttpClient staff, string email)
    {
        var learnerId = await fixture.LearnerIdAsync("A-231");

        using var response = await staff.PostAsJsonAsync("/api/admin/people/parents", new
        {
            firstName = "Zanele",
            surname = "Test",
            email,
            phone = "071 555 0199",
            relationship = "mother",
            learnerIds = new[] { learnerId },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var invite = body.GetProperty("invite");

        return (body.GetProperty("userId").GetGuid(), invite.GetProperty("code").GetString()!);
    }

    private static async Task<HttpResponseMessage> RedeemAsync(HttpClient client, string email, string code, string password = NewPassword) =>
        await client.PostAsJsonAsync("/api/auth/redeem-code", new { email, code, newPassword = password });

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password, returnRefreshTokenInBody = true });

    // ------------------------------------------------------------ the journey

    [Fact]
    public async Task A_parent_added_by_staff_redeems_the_invite_and_signs_in()
    {
        using var staff = await fixture.SignInAsync(Manager);
        var email = UniqueEmail("parent");
        var (_, code) = await AddParentAsync(staff, email);

        using var client = Anonymous();

        // Invited is not active: the account cannot be used until the person
        // has chosen their own password.
        using (var early = await SignInAsync(client, email, NewPassword))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);
        }

        using (var redeemed = await RedeemAsync(client, email, code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)))
        {
            // Case, spaces and the dash do not matter: codes get read out over the phone.
            Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
        }

        using var signedIn = await SignInAsync(client, email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        var user = (await signedIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user");
        Assert.Equal("Parent", user.GetProperty("role").GetString());
        Assert.NotEqual(JsonValueKind.Null, user.GetProperty("parentId").ValueKind);
    }

    [Fact]
    public async Task A_code_works_once()
    {
        using var staff = await fixture.SignInAsync(Manager);
        var email = UniqueEmail("once");
        var (_, code) = await AddParentAsync(staff, email);

        using var client = Anonymous();

        using (var first = await RedeemAsync(client, email, code))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using var second = await RedeemAsync(client, email, code, "A different password entirely");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_guesses_kill_the_code()
    {
        using var staff = await fixture.SignInAsync(Manager);
        var email = UniqueEmail("guess");
        var (_, code) = await AddParentAsync(staff, email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            // A fresh address each time: this is about the code's own limit,
            // not the per-address rate limiter.
            using var client = Anonymous();
            using var wrong = await RedeemAsync(client, email, "AAAAA-AAAAA");

            Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        }

        using var late = Anonymous();
        using var right = await RedeemAsync(late, email, code);

        Assert.Equal(HttpStatusCode.Conflict, right.StatusCode);
    }

    [Fact]
    public async Task An_unknown_email_and_a_wrong_code_get_the_same_answer()
    {
        using var staff = await fixture.SignInAsync(Manager);
        var email = UniqueEmail("same");
        await AddParentAsync(staff, email);

        using var client = Anonymous();
        using var unknown = await RedeemAsync(client, UniqueEmail("nobody"), "BBBBB-BBBBB");
        using var wrongCode = await RedeemAsync(client, email, "BBBBB-BBBBB");

        Assert.Equal(unknown.StatusCode, wrongCode.StatusCode);
        Assert.Equal(
            (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(),
            (await wrongCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Issuing_a_new_code_cancels_the_old_one()
    {
        using var staff = await fixture.SignInAsync(Manager);
        var email = UniqueEmail("reissue");
        var (userId, first) = await AddParentAsync(staff, email);

        using var reissued = await staff.PostAsync(new Uri($"/api/admin/people/{userId}/code", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, reissued.StatusCode);
        var second = (await reissued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

        using var client = Anonymous();

        using (var old = await RedeemAsync(client, email, first))
        {
            Assert.Equal(HttpStatusCode.Conflict, old.StatusCode);
        }

        using var current = await RedeemAsync(client, email, second);
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    [Fact]
    public async Task A_reset_signs_out_every_existing_session()
    {
        using var staff = await fixture.SignInAsync(Manager);
        var email = UniqueEmail("reset");
        var (userId, invite) = await AddParentAsync(staff, email);

        using var client = Anonymous();
        (await RedeemAsync(client, email, invite)).Dispose();

        using var session = await SignInAsync(client, email, NewPassword);
        var refresh = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();

        // The parent is now active, so what staff issue is a reset, not an invite.
        using var issued = await staff.PostAsync(new Uri($"/api/admin/people/{userId}/code", UriKind.Relative), null);
        var reset = await issued.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PasswordReset", reset.GetProperty("purpose").GetString());

        using (var redeemed = await RedeemAsync(client, email, reset.GetProperty("code").GetString()!, "A brand new password now"))
        {
            Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
        }

        using var stale = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh, returnRefreshTokenInBody = true });
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        using var oldPassword = await SignInAsync(Anonymous(), email, NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
    }

    [Fact]
    public async Task A_learner_without_a_login_gets_a_reference_and_is_enrolled()
    {
        using var staff = await fixture.SignInAsync(Manager);
        using var anonymous = fixture.CreateClient();
        var programmes = await anonymous.GetFromJsonAsync<JsonElement>("/api/public/programmes");
        var programmeId = programmes[0].GetProperty("programmeId").GetGuid();

        using var response = await staff.PostAsJsonAsync("/api/admin/people/learners", new
        {
            firstName = "Lindiwe",
            surname = $"Test{Guid.NewGuid():N}"[..20],
            dateOfBirth = "2012-04-18",
            grade = 8,
            school = "Bophelong Secondary",
            programmeId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var reference = body.GetProperty("referenceCode").GetString()!;

        Assert.StartsWith("A-", reference, StringComparison.Ordinal);
        // No login, so no invitation. The API leaves empty fields out entirely.
        Assert.False(body.TryGetProperty("invite", out var invite) && invite.ValueKind != JsonValueKind.Null);

        // Enrolled, so the learner roll (which lists active enrolments) shows them.
        var roll = await staff.GetFromJsonAsync<JsonElement>($"/api/admin/learners?query={reference}");
        Assert.Equal(1, roll.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task An_email_that_already_has_an_account_is_refused()
    {
        using var staff = await fixture.SignInAsync(Manager);
        var learnerId = await fixture.LearnerIdAsync("A-231");

        using var response = await staff.PostAsJsonAsync("/api/admin/people/parents", new
        {
            firstName = "Nomvula",
            surname = "Again",
            email = ParentAccount.ToUpperInvariant(),
            phone = "071 555 0100",
            learnerIds = new[] { learnerId },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ------------------------------------------------------ the boundaries

    [Fact]
    public async Task Programme_staff_cannot_add_staff()
    {
        using var staff = await fixture.SignInAsync(Manager);

        using var response = await staff.PostAsJsonAsync("/api/admin/people/staff", new
        {
            firstName = "Not",
            surname = "Allowed",
            email = UniqueEmail("staff"),
            jobTitle = "Coordinator",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Programme_staff_cannot_reset_the_safeguarding_leads_password()
    {
        using var admin = await fixture.SignInAsync(Admin);
        var found = await admin.GetFromJsonAsync<JsonElement>("/api/admin/people?query=safeguarding@");
        var leadId = found.GetProperty("items")[0].GetProperty("userId").GetGuid();

        // The case this rule exists for: a reset code for the lead would let
        // programme staff sign in as them and read the safeguarding queue.
        using var staff = await fixture.SignInAsync(Manager);
        using var response = await staff.PostAsync(new Uri($"/api/admin/people/{leadId}/code", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_parent_cannot_reach_the_people_endpoints()
    {
        using var parent = await fixture.SignInAsync(ParentAccount);

        using var list = await parent.GetAsync(new Uri("/api/admin/people", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task There_can_only_be_one_safeguarding_lead()
    {
        using var admin = await fixture.SignInAsync(Admin);

        using var response = await admin.PostAsJsonAsync("/api/admin/people/staff", new
        {
            firstName = "Second",
            surname = "Lead",
            email = UniqueEmail("lead"),
            jobTitle = "Safeguarding Lead",
            isSafeguardingLead = true,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_administrator_can_add_staff_who_then_activate()
    {
        using var admin = await fixture.SignInAsync(Admin);
        var email = UniqueEmail("coordinator");

        using var response = await admin.PostAsJsonAsync("/api/admin/people/staff", new
        {
            firstName = "Thabo",
            surname = "Coordinator",
            email,
            jobTitle = "Programme Coordinator",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invite").GetProperty("code").GetString()!;

        using var client = Anonymous();
        using var redeemed = await RedeemAsync(client, email, code);

        Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
    }
}

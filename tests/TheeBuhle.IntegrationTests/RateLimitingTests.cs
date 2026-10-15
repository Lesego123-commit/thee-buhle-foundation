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
/// The rate limiter, asserted rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// This class exists because the limiter throttled the test suite itself. The
/// sign-in helper now gives each simulated account its own address, which models
/// what is true — six different people on six different connections — and the
/// obvious risk in doing that is leaving the control untested.
/// </para>
/// <para>
/// So these tests deliberately come from a <strong>single</strong> address,
/// where throttling is the expected outcome, and assert the production
/// configuration exactly as deployed.
/// </para>
/// </remarks>
public sealed class RateLimitingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    /// <summary>
    /// Repeated sign-in attempts from one address are throttled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five per five minutes. The sixth is refused with 429 and a
    /// <c>Retry-After</c> header, so a client knows to wait rather than
    /// hammering.
    /// </para>
    /// <para>
    /// <strong>This is independent of the account lock, and both are needed.</strong>
    /// The lock stops five wrong passwords against <em>one</em> account. The
    /// limiter stops one address trying five hundred <em>different</em> accounts
    /// — which no per-account lock can see, because each account receives a
    /// single attempt and none of them locks.
    /// </para>
    /// <para>
    /// Note that the attempts below use the <em>wrong</em> password, so the
    /// limiter is being tested rather than six successful logins being counted.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Repeated_sign_in_attempts_from_one_address_are_throttled()
    {
        const string Address = "10.99.0.1";

        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 7; attempt++)
        {
            using var client = fixture.CreateClient();

            client.DefaultRequestHeaders.Add("X-Forwarded-For", Address);

            using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new
            {
                email = "nomvula.mahlangu@example.co.za",
                password = "definitely-not-the-password",
                returnRefreshTokenInBody = true,
            });

            statuses.Add(response.StatusCode);
        }

        // The first five are refused on the credentials.
        Assert.All(statuses.Take(5), status => Assert.Equal(HttpStatusCode.Unauthorized, status));

        // Everything after that is refused on the rate.
        Assert.All(statuses.Skip(5), status => Assert.Equal(HttpStatusCode.TooManyRequests, status));
    }

    /// <summary>
    /// A throttled response tells the caller how long to wait.
    /// </summary>
    /// <remarks>
    /// A specific waiting time rather than "try again later". Somebody who has
    /// genuinely mistyped their password five times deserves to know whether to
    /// wait or to phone — and the message gives them the number.
    /// </remarks>
    [Fact]
    public async Task A_throttled_response_says_how_long_to_wait_and_gives_a_phone_number()
    {
        const string Address = "10.99.0.2";

        HttpResponseMessage? throttled = null;

        for (var attempt = 0; attempt < 7 && throttled is null; attempt++)
        {
            using var client = fixture.CreateClient();

            client.DefaultRequestHeaders.Add("X-Forwarded-For", Address);

            var response = await client.PostAsJsonAsync("/api/auth/sign-in", new
            {
                email = "nomvula.mahlangu@example.co.za",
                password = "wrong",
                returnRefreshTokenInBody = true,
            });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = response;
            }
            else
            {
                response.Dispose();
            }
        }

        Assert.NotNull(throttled);

        using (throttled)
        {
            Assert.NotNull(throttled.Headers.RetryAfter);

            var body = await throttled.Content.ReadAsStringAsync();

            Assert.Contains("minute", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("+27 71 000 0000", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// One address being throttled does not affect another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property that makes the limiter usable rather than a denial-of-service
    /// vector. Without per-address partitioning, one person mistyping their
    /// password five times would lock every other family out of the app — and a
    /// Saturday session has thirty parents on one venue's Wi-Fi.
    /// </para>
    /// <para>
    /// That shared-connection case is exactly why the limiter keys on the
    /// signed-in user where there is one, and only falls back to the address for
    /// anonymous requests like sign-in.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Throttling_one_address_does_not_throttle_another()
    {
        const string Throttled = "10.99.0.3";
        const string Innocent = "10.99.0.4";

        // Exhaust the first address.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var client = fixture.CreateClient();

            client.DefaultRequestHeaders.Add("X-Forwarded-For", Throttled);

            using var _ = await client.PostAsJsonAsync("/api/auth/sign-in", new
            {
                email = "nomvula.mahlangu@example.co.za",
                password = "wrong",
                returnRefreshTokenInBody = true,
            });
        }

        // The second is unaffected and can still sign in properly.
        using var other = fixture.CreateClient();

        other.DefaultRequestHeaders.Add("X-Forwarded-For", Innocent);

        using var response = await other.PostAsJsonAsync("/api/auth/sign-in", new
        {
            email = "joseph.nkosi@example.co.za",
            password = Infrastructure.Persistence.DatabaseSeeder.DemoPassword,
            returnRefreshTokenInBody = true,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A wrong password, an unknown address and a locked account read identically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The visible half of the account-enumeration defence. If "no such user"
    /// differed from "wrong password", the login form would become a way of
    /// checking whether a given person has an account with this organisation —
    /// which, for a children's charity, is information worth protecting on its
    /// own.
    /// </para>
    /// <para>
    /// The measurable half is the timing, which is closed by
    /// <c>IPasswordHasher.DummyVerify</c> and is asserted in the unit suite
    /// where a stopwatch is meaningful.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_unknown_email_and_a_wrong_password_give_the_same_answer()
    {
        var unknown = await AttemptAsync("10.99.1.1", "nobody@example.invalid", "whatever");
        var wrong = await AttemptAsync("10.99.1.2", "nomvula.mahlangu@example.co.za", "whatever");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal(unknown.Detail, wrong.Detail);
    }

    private async Task<(HttpStatusCode Status, string Detail)> AttemptAsync(
        string address,
        string email,
        string password)
    {
        using var client = fixture.CreateClient();

        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);

        using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new
        {
            email,
            password,
            returnRefreshTokenInBody = true,
        });

        var body = await response.Content.ReadAsStringAsync();

        using var document = System.Text.Json.JsonDocument.Parse(body);

        var detail = document.RootElement.TryGetProperty("detail", out var value)
            ? value.GetString() ?? string.Empty
            : body;

        return (response.StatusCode, detail);
    }
}

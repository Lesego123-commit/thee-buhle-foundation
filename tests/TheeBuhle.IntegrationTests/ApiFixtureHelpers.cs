// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Microsoft (2026) ASP.NET Core documentation, including WebApplicationFactory integration testing.
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.IntegrationTests;

/// <summary>
/// Helpers that make an integration test read as the scenario it is about.
/// </summary>
/// <remarks>
/// <para>
/// All of these go through the real API rather than reaching into the database,
/// with one exception that is marked. Signing in through
/// <c>POST /api/auth/sign-in</c> rather than minting a token in the test means
/// the tests exercise the authentication path too — and it means a test cannot
/// accidentally grant itself a role the login flow would not.
/// </para>
/// <para>
/// The rate limiter is the one wrinkle: sign-in allows five attempts per five
/// minutes per address, and a test class doing more than that would throttle
/// itself. Tokens are therefore cached per email for the life of the fixture.
/// </para>
/// </remarks>
public static class ApiFixtureHelpers
{
    /// <summary>The password every seeded demonstration account shares.</summary>
    private const string DemoPassword = DatabaseSeeder.DemoPassword;

    private static readonly Dictionary<(ApiFixture Fixture, string Email), string> TokenCache = [];

    private static readonly SemaphoreSlim CacheGate = new(1, 1);

    /// <summary>
    /// Signs in and returns a client carrying the bearer token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cached per email per fixture. Not an optimisation: the sign-in endpoint
    /// allows five attempts per five minutes per address, and a test class that
    /// signed in a dozen times would be rate-limited into failing for a reason
    /// that has nothing to do with the code under test.
    /// </para>
    /// <para>
    /// The limiter being that strict is correct — it is what stops credential
    /// stuffing — so the tests work with it rather than turning it off, which
    /// would leave the production configuration untested.
    /// </para>
    /// </remarks>
    public static async Task<HttpClient> SignInAsync(this ApiFixture fixture, string email)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var token = await GetTokenAsync(fixture, email);

        var client = fixture.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>The learner's id, by reference code.</summary>
    /// <remarks>
    /// Read from the database rather than the API, and deliberately so: there is
    /// no endpoint that maps a pseudonym to an id for an arbitrary caller, which
    /// is itself a privacy decision. A test needs the id to build a URL, so it
    /// reads it directly rather than the API growing a lookup nobody needs.
    /// </remarks>
    public static async Task<Guid> LearnerIdAsync(this ApiFixture fixture, string referenceCode)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        await using var context = fixture.CreateDbContext();

        return await context.Learners
            .Where(learner => learner.ReferenceCode == referenceCode)
            .Select(learner => learner.Id)
            .FirstAsync();
    }

    /// <summary>
    /// The mentor's assignment id for one of their mentees.
    /// </summary>
    /// <remarks>
    /// Through the mentor's own dashboard, because that is how the app gets it —
    /// so a change that broke the dashboard's shape would fail these tests too,
    /// which is the right coupling to have.
    /// </remarks>
    public static async Task<Guid> AssignmentForAsync(
        this HttpClient mentor,
        string referenceCode)
    {
        ArgumentNullException.ThrowIfNull(mentor);

        var dashboard = await mentor.GetFromJsonAsync<MentorDashboardRow>("/api/mentor/dashboard")
            ?? throw new InvalidOperationException("The mentor dashboard returned nothing.");

        var mentee = dashboard.Mentees.FirstOrDefault(m => m.ReferenceCode == referenceCode)
            ?? throw new InvalidOperationException(
                $"This mentor has no mentee {referenceCode}. "
                + $"They have: {string.Join(", ", dashboard.Mentees.Select(m => m.ReferenceCode))}.");

        return mentee.AssignmentId;
    }

    private static async Task<string> GetTokenAsync(ApiFixture fixture, string email)
    {
        await CacheGate.WaitAsync();

        try
        {
            if (TokenCache.TryGetValue((fixture, email), out var cached))
            {
                return cached;
            }

            using var client = fixture.CreateClient();

            // ----------------------------------------------------------------
            //  A distinct forwarded-for address per account.
            //
            //  The sign-in limiter allows five attempts per five minutes per
            //  address, and every request from the test host arrives from the
            //  same synthetic one — so a suite signing in as six different
            //  people was throttled after the fifth and failed for a reason
            //  unrelated to the code. The limiter was doing its job.
            //
            //  Giving each account its own address models what is actually
            //  true: these are six different people on six different
            //  connections. It does not weaken the control, and the production
            //  configuration stays exactly as deployed — which matters, because
            //  raising the limit for tests would leave the real setting
            //  untested.
            //
            //  The limiter's own behaviour is asserted directly in
            //  RateLimitingTests, from a single address, where throttling is the
            //  expected outcome.
            // ----------------------------------------------------------------
            client.DefaultRequestHeaders.Add("X-Forwarded-For", AddressFor(email));

            using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new
            {
                email,
                password = DemoPassword,
                returnRefreshTokenInBody = true,
            });

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();

                throw new InvalidOperationException(
                    $"Signing in as {email} failed with {(int)response.StatusCode}: {body}");
            }

            var payload = await response.Content.ReadFromJsonAsync<SignInRow>()
                ?? throw new InvalidOperationException("The sign-in response was empty.");

            TokenCache[(fixture, email)] = payload.AccessToken;

            return payload.AccessToken;
        }
        finally
        {
            CacheGate.Release();
        }
    }

    /// <summary>A stable, distinct private address per account.</summary>
    /// <remarks>
    /// Derived from the email so it is the same across a run, and in the
    /// 10.0.0.0/8 private range so it is obviously synthetic.
    /// </remarks>
    private static string AddressFor(string email)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(email));

        return $"10.{hash[0]}.{hash[1]}.{hash[2]}";
    }

    private sealed record SignInRow(string AccessToken, string? RefreshToken, int ExpiresIn);

    private sealed record MentorDashboardRow(string DisplayName, List<MenteeRow> Mentees);

    private sealed record MenteeRow(Guid AssignmentId, Guid LearnerId, string ReferenceCode);
}

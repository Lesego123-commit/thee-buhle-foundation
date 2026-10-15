// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using Microsoft.Extensions.Configuration;
using TheeBuhle.Infrastructure.Security;

namespace TheeBuhle.UnitTests.Security;

/// <summary>
/// Tests for the startup check on the JWT settings.
/// </summary>
/// <remarks>
/// The settings are bound from configuration exactly as the API binds them,
/// because the failure worth catching is the one the binder produces: a brand
/// new App Service has no <c>Jwt__SigningKey</c> at all, and the property
/// arrives as null despite being declared <c>required</c>.
/// </remarks>
public sealed class JwtOptionsTests
{
    private static JwtOptions Bind(string? signingKey)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://theebuhlefoundation.co.za",
            ["Jwt:Audience"] = "theebuhle-clients",
        };

        if (signingKey is not null)
        {
            values["Jwt:SigningKey"] = signingKey;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection("Jwt")
            .Get<JwtOptions>()!;
    }

    [Fact]
    public void A_missing_key_fails_with_a_message_that_names_the_setting()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Bind(signingKey: null).Validate());

        Assert.Contains("Jwt__SigningKey", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_key_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Bind("too-short").Validate());

        Assert.Contains("at least 32 bytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_32_byte_key_is_accepted()
    {
        Bind(new string('k', 32)).Validate();
    }
}

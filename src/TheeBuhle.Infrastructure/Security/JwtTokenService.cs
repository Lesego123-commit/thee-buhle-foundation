// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Jones, Bradley and Sakimura (2015) RFC 7519: JSON Web Token.
// =============================================================================

using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Infrastructure.Security;

/// <summary>
/// Issues the short-lived JWT access token and the long-lived opaque refresh
/// token.
/// </summary>
/// <remarks>
/// <para>
/// The two are deliberately different <em>kinds</em> of thing, and the asymmetry
/// is the design:
/// </para>
/// <list type="bullet">
/// <item>
/// The <strong>access token</strong> is a signed JWT with a 15-minute life. It
/// is self-describing, so the API validates it without a database round trip on
/// every request — which is what makes it cheap — and it cannot be revoked,
/// which is what makes 15 minutes the right number rather than an hour.
/// </item>
/// <item>
/// The <strong>refresh token</strong> is 32 random bytes with no structure at
/// all. It means nothing except "this row exists in the database", so it can be
/// revoked instantly, and only its SHA-256 hash is stored — a stolen backup is
/// not a set of working sessions.
/// </item>
/// </list>
/// <para>
/// The refresh token is <em>not</em> a JWT, and that is on purpose. A JWT
/// refresh token has to be checked against a revocation list anyway to be
/// revocable, at which point its self-describing property is paying for nothing
/// and its claims are one more thing that can be trusted by mistake.
/// </para>
/// </remarks>
/// <remarks>Tokens follow RFC 7519 (Jones, Bradley and Sakimura, 2015); see docs/references.md.</remarks>
public sealed class JwtTokenService(JwtOptions options, IClock clock) : ITokenService
{
    private const int RefreshTokenBytes = 32;

    private readonly SigningCredentials credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    public string IssueAccessToken(Guid userId, Role role, TimeSpan lifetime)
    {
        var now = clock.Now;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Subject, userId.ToString()),
            new(ClaimTypes.Role, role.ToString()),

            // A unique id per token, so an individual token can be named in a
            // log or a revocation list without logging the token itself.
            new(JwtRegisteredClaimNames.JwtId, Guid.CreateVersion7().ToString()),
        };

        // Note what is *not* in here: no email, no display name, no learner or
        // parent ids. A JWT is signed, not encrypted — anybody holding it can
        // read every claim — so it carries an identifier and a role and nothing
        // that would be personal information sitting in a browser's storage.
        //
        // The role is present because it is needed on every request and is not
        // sensitive. Everything else is rebuilt from the database per request,
        // which is also what makes a suspension take effect immediately rather
        // than in fifteen minutes.
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.Add(lifetime).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, string Hash) IssueRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(RefreshTokenBytes);

        // Base64url, so the value survives a URL, a header and a JSON body
        // without escaping — and without the '+' that turns into a space when
        // something helpfully form-decodes it.
        var token = Base64UrlEncoder.Encode(bytes);

        return (token, HashRefreshToken(token));
    }

    /// <summary>
    /// Hashes a presented refresh token for lookup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain SHA-256, with no salt and no work factor — which would be wrong for
    /// a password and is right here. A refresh token is 32 bytes from a
    /// cryptographic RNG, so there is no dictionary to attack and nothing for a
    /// work factor to slow down; the hash exists only so that a leaked database
    /// does not contain usable tokens.
    /// </para>
    /// <para>
    /// It also has to be deterministic, because the token is looked up <em>by</em>
    /// its hash. A salted hash cannot be, which is exactly why passwords are
    /// verified one at a time and refresh tokens are not.
    /// </para>
    /// </remarks>
    public string HashRefreshToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>The claim names, spelled out rather than pulled from a constants class.</summary>
    private static class JwtRegisteredClaimNames
    {
        public const string Subject = "sub";

        public const string JwtId = "jti";
    }
}

/// <summary>
/// Token signing and validation settings.
/// </summary>
/// <remarks>
/// The signing key comes from configuration and is never in source: an Azure App
/// Service application setting in production, user-secrets locally. It is the
/// one value in this system that, if leaked, lets anybody mint a token for any
/// user — so it is treated the way a private key is.
/// </remarks>
public sealed record JwtOptions
{
    /// <summary>At least 32 bytes, because HMAC-SHA256 has a 256-bit key.</summary>
    public required string SigningKey { get; init; }

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    /// <summary>
    /// Validates the settings at startup rather than at first sign-in.
    /// </summary>
    /// <remarks>
    /// A signing key that is too short does not fail when it is configured — it
    /// fails on the first login attempt, in production, with an exception nobody
    /// expected. Checking at startup turns that into a deployment that refuses
    /// to start, which is a much better failure.
    /// </remarks>
    public void Validate()
    {
        // `required` is a compile-time check only: the configuration binder
        // leaves the property null when the setting is absent, which is the
        // usual state of a freshly created App Service.
        if (string.IsNullOrEmpty(SigningKey))
        {
            throw new InvalidOperationException(
                "No JWT signing key is configured. Set Jwt__SigningKey (at least 32 random bytes) "
                + "in the App Service settings, or with `dotnet user-secrets` locally.");
        }

        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "The JWT signing key must be at least 32 bytes. "
                + $"The configured key is {Encoding.UTF8.GetByteCount(SigningKey).ToString(CultureInfo.InvariantCulture)} bytes.");
        }

        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException(
                "The JWT issuer and audience must both be configured. "
                + "Without them a token minted for another service would validate here.");
        }
    }
}

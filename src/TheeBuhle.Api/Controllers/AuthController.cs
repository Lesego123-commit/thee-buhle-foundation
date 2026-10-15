// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Services;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// Sign in, renew a session, sign out, change a password.
/// </summary>
/// <remarks>
/// <para>
/// The refresh token comes back in an <strong>httpOnly cookie</strong> and the
/// access token in the response body. That split is the whole design of this
/// controller:
/// </para>
/// <list type="bullet">
/// <item>
/// The <strong>refresh token</strong> is long-lived and therefore the valuable
/// one, so it goes somewhere JavaScript cannot read. An XSS payload on the
/// admin console can steal anything in <c>localStorage</c>; it cannot read an
/// httpOnly cookie.
/// </item>
/// <item>
/// The <strong>access token</strong> has to be readable by the client to put in
/// an <c>Authorization</c> header, so it cannot be hidden — which is exactly why
/// it lives fifteen minutes and cannot be used to obtain a new one.
/// </item>
/// </list>
/// <para>
/// The mobile app is the exception and asks for the refresh token in the body,
/// because it has no cookie jar and stores it in the OS keychain instead — which
/// is a stronger place than a cookie, not a weaker one.
/// </para>
/// </remarks>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    AuthService auth,
    AccountService accounts,
    CurrentUserAccessor currentUser,
    AuthOptions options) : ControllerBase
{
    private const string RefreshCookie = "tb_refresh";

    /// <summary>Signs in with an email address and password.</summary>
    /// <remarks>
    /// Answers with the identical message for a wrong password, an unknown
    /// address and a locked account — and takes about the same time in all three
    /// cases, so neither the wording nor the response time reveals whether an
    /// account exists.
    /// </remarks>
    /// <response code="200">Signed in. The refresh token is in an httpOnly cookie.</response>
    /// <response code="401">Refused, with one uniform message.</response>
    /// <response code="429">Too many attempts from this address.</response>
    [HttpPost("sign-in")]
    [EnableRateLimiting(RateLimiting.SignIn)]
    [ProducesResponseType(typeof(SignInResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SignInResponse>> SignIn(
        [FromBody] SignInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pair = await auth.SignInAsync(
            request.Email.Trim(),
            request.Password,
            DescribeDevice(),
            cancellationToken);

        return Ok(Respond(pair, request.ReturnRefreshTokenInBody));
    }

    /// <summary>Exchanges a refresh token for a new pair.</summary>
    /// <remarks>
    /// <para>
    /// The refresh token is <strong>rotated</strong>: the presented one is
    /// revoked and a new one issued. So a token is usable exactly once, and a
    /// stolen copy is detectable — presenting an already-used token revokes the
    /// whole family and signs both the thief and the genuine user out.
    /// </para>
    /// <para>
    /// Read from the cookie when there is one and from the body otherwise, which
    /// is what lets the website and the mobile app share this endpoint.
    /// </para>
    /// </remarks>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(SignInResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SignInResponse>> Refresh(
        [FromBody] RefreshRequest? request,
        CancellationToken cancellationToken)
    {
        var token = Request.Cookies[RefreshCookie] ?? request?.RefreshToken;

        if (string.IsNullOrWhiteSpace(token))
        {
            // The same message the service uses for an invalid token, so this
              // endpoint does not distinguish "you sent nothing" from "what you
              // sent was revoked".
            return Unauthorized(new { detail = "That session could not be renewed. Please sign in again." });
        }

        var pair = await auth.RefreshAsync(token, DescribeDevice(), cancellationToken);

        return Ok(Respond(pair, request?.ReturnRefreshTokenInBody ?? false));
    }

    /// <summary>Signs out of this session only.</summary>
    /// <remarks>
    /// Always answers 204, whether or not there was a session to end. A sign-out
    /// that reported "you were not signed in" would be a way of testing whether
    /// a stolen refresh token is still live.
    /// </remarks>
    [HttpPost("sign-out")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOut(
        [FromBody] RefreshRequest? request,
        CancellationToken cancellationToken)
    {
        var token = Request.Cookies[RefreshCookie] ?? request?.RefreshToken;

        if (!string.IsNullOrWhiteSpace(token))
        {
            await auth.SignOutAsync(token, cancellationToken);
        }

        ClearRefreshCookie();

        return NoContent();
    }

    /// <summary>Signs out of every device.</summary>
    /// <remarks>
    /// The button somebody presses when they think their account has been
    /// compromised, so it revokes every refresh token rather than only this
    /// one's.
    /// </remarks>
    [HttpPost("sign-out-everywhere")]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutEverywhere(CancellationToken cancellationToken)
    {
        await auth.SignOutEverywhereAsync(currentUser.Require().UserId, cancellationToken);

        ClearRefreshCookie();

        return NoContent();
    }

    /// <summary>
    /// Redeems a one-time code from the foundation: accepts an invitation, or
    /// completes a password reset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anonymous, because the person has no password yet. Rate-limited with
    /// sign-in, and each code dies after five wrong guesses, so it cannot be
    /// used to guess codes.
    /// </para>
    /// <para>
    /// Every failure gets the same message, whether the email is unknown, the
    /// code is wrong, or it has expired or been used. Setting the password
    /// signs out every existing session for the account.
    /// </para>
    /// </remarks>
    /// <response code="204">The password is set. Sign in with it.</response>
    /// <response code="409">The code was not accepted, or the password is too short.</response>
    [HttpPost("redeem-code")]
    [EnableRateLimiting(RateLimiting.SignIn)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RedeemCode(
        [FromBody] RedeemCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await accounts.RedeemAsync(request.Email, request.Code, request.NewPassword, cancellationToken);

        return NoContent();
    }

    /// <summary>Changes the signed-in user's password.</summary>
    /// <remarks>
    /// <para>
    /// The current password is required even though the caller is already
    /// authenticated. An access token in the wrong hands should not be enough to
    /// take an account over permanently — and without this, a fifteen-minute
    /// stolen token becomes indefinite control.
    /// </para>
    /// <para>
    /// A successful change revokes every refresh token, including this one, so
    /// the caller has to sign in again. That is the point: if the password was
    /// changed because it was compromised, an attacker holding a refresh token
    /// must not keep a live session.
    /// </para>
    /// </remarks>
    [HttpPost("change-password")]
    [RequireUser]
    [EnableRateLimiting(RateLimiting.SignIn)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await auth.ChangePasswordAsync(
            currentUser.Require(),
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        ClearRefreshCookie();

        return NoContent();
    }

    /// <summary>Who the caller is, as the server sees them right now.</summary>
    /// <remarks>
    /// Rebuilt from the database rather than decoded from the token, so a client
    /// calling this after a role change or a suspension gets the truth rather
    /// than what its token claims.
    /// </remarks>
    [HttpGet("me")]
    [RequireUser]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    public ActionResult<CurrentUserResponse> Me()
    {
        var user = currentUser.Require();

        return Ok(new CurrentUserResponse(
            user.UserId,
            user.Email,
            user.DisplayName,
            user.Role.ToString(),
            user.RoleLabel,
            user.ParentId,
            user.LearnerId,
            user.MentorId,
            user.StaffId));
    }

    private SignInResponse Respond(TokenPair pair, bool refreshTokenInBody)
    {
        if (!refreshTokenInBody)
        {
            SetRefreshCookie(pair.RefreshToken);
        }

        return new SignInResponse(
            pair.AccessToken,
            // Null for a browser, so the value the cookie holds is never also
            // sitting in a variable that JavaScript can read.
            refreshTokenInBody ? pair.RefreshToken : null,
            pair.ExpiresIn,
            new CurrentUserResponse(
                pair.User.UserId,
                pair.User.Email,
                pair.User.DisplayName,
                pair.User.Role.ToString(),
                pair.User.RoleLabel,
                pair.User.ParentId,
                pair.User.LearnerId,
                pair.User.MentorId,
                pair.User.StaffId));
    }

    /// <summary>
    /// Sets the refresh cookie.
    /// </summary>
    /// <remarks>
    /// <para>Four flags, each closing something specific:</para>
    /// <list type="bullet">
    /// <item><c>HttpOnly</c> — JavaScript cannot read it, so an XSS payload
    /// cannot steal the long-lived credential.</item>
    /// <item><c>Secure</c> — never sent over plain HTTP.</item>
    /// <item><c>SameSite=Strict</c> — not sent on a cross-site request at all,
    /// which is what makes CSRF against the refresh endpoint impossible rather
    /// than merely difficult. Strict rather than Lax because nothing in this
    /// application needs the cookie on a top-level navigation from elsewhere.</item>
    /// <item><c>Path</c> — scoped to the auth routes, so it is not attached to
    /// every request for a stylesheet.</item>
    /// </list>
    /// </remarks>
    private void SetRefreshCookie(string token) =>
        Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.Add(options.RefreshTokenLifetime),
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            // The flags must match the ones it was set with, or the browser
            // treats it as a different cookie and the original survives.
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
        });

    /// <summary>
    /// A short description of the device, for the sessions list.
    /// </summary>
    /// <remarks>
    /// The user-agent, truncated. Enough for somebody reviewing their sessions
    /// to recognise "the phone" from "the library computer", and short enough
    /// not to store a 400-character string per sign-in.
    /// </remarks>
    private string? DescribeDevice()
    {
        var agent = Request.Headers.UserAgent.FirstOrDefault();

        return string.IsNullOrWhiteSpace(agent)
            ? null
            : agent.Length <= 120 ? agent : agent[..120];
    }
}

public sealed record SignInRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(190)]
    public required string Email { get; init; }

    /// <summary>
    /// No maximum length beyond a sanity bound.
    /// </summary>
    /// <remarks>
    /// Argon2 hashes any length to 32 bytes, so a long passphrase costs nothing
    /// — and a low maximum is a genuine weakness that exists only because some
    /// systems store passwords in a fixed-width column. 200 characters is a
    /// bound against somebody posting a megabyte, not a policy about passwords.
    /// </remarks>
    [Required]
    [MaxLength(200)]
    public required string Password { get; init; }

    /// <summary>
    /// True for the mobile app, which stores the refresh token in the OS
    /// keychain and has no cookie jar.
    /// </summary>
    public bool ReturnRefreshTokenInBody { get; init; }
}

public sealed record RefreshRequest
{
    public string? RefreshToken { get; init; }

    public bool ReturnRefreshTokenInBody { get; init; }
}

public sealed record ChangePasswordRequest
{
    [Required]
    [MaxLength(200)]
    public required string CurrentPassword { get; init; }

    /// <summary>
    /// At least twelve characters.
    /// </summary>
    /// <remarks>
    /// Length and nothing else — no required symbol, no required digit. Composition
    /// rules reliably produce <c>Password1!</c>, which is shorter and weaker than
    /// a passphrase somebody can actually remember, and they are the reason
    /// people write passwords down.
    /// </remarks>
    [Required]
    [MinLength(12)]
    [MaxLength(200)]
    public required string NewPassword { get; init; }
}

public sealed record RedeemCodeRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(190)]
    public required string Email { get; init; }

    /// <summary>As given, e.g. <c>K7MPQ-2XRT9</c>. Case, spaces and the dash do not matter.</summary>
    [Required]
    [MaxLength(40)]
    public required string Code { get; init; }

    /// <summary>At least twelve characters, the same rule as changing a password.</summary>
    [Required]
    [MinLength(12)]
    [MaxLength(200)]
    public required string NewPassword { get; init; }
}

public sealed record SignInResponse(
    string AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    CurrentUserResponse User);

public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    string RoleLabel,
    Guid? ParentId,
    Guid? LearnerId,
    Guid? MentorId,
    Guid? StaffId);

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Services;

/// <summary>
/// One exception for every authentication failure, with one default message.
/// </summary>
/// <remarks>
/// "Those details are not correct" covers a wrong password, an unknown email, a
/// suspended account and a locked account. That is deliberate: a sign-in form
/// that says "no such user" is an account-enumeration tool, and one that says
/// "account locked" tells an attacker their guessing worked well enough to trip
/// the lock. The real reason is written to the log, where it helps the
/// foundation and not an attacker.
/// </remarks>
public sealed class AuthenticationException(string message = "Those sign-in details are not correct.")
    : Exception(message);

/// <summary>Authentication settings, bound from configuration.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Short, because the access token is not revocable once issued.</summary>
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);

    public int LockThreshold { get; init; } = 5;

    public int LockMinutes { get; init; } = 15;
}

/// <summary>
/// Authentication: sign in, refresh, sign out, change password.
/// </summary>
/// <remarks>
/// <para>The decisions worth calling out, all commitments from Task 1 section 9.3.13.4:</para>
/// <list type="bullet">
/// <item><strong>Argon2id</strong> for password hashing, with cost parameters in
/// configuration so they can be raised as the host gets faster without a code
/// change. A hash made with weaker parameters is upgraded on next sign-in rather
/// than leaving old accounts on old costs forever.</item>
///
/// <item><strong>A uniform failure.</strong> Wrong password, unknown email,
/// suspended account and locked account all produce the same
/// <see cref="AuthenticationException"/> with the same message.</item>
///
/// <item><strong>A dummy verify on unknown emails.</strong> Without it, an
/// unknown address returns in microseconds and a known one takes as long as
/// Argon2id does — so the <em>timing</em> enumerates accounts even though the
/// message does not.</item>
///
/// <item><strong>Short access token, rotating refresh token.</strong> The access
/// token lives fifteen minutes and is not stored server-side. The refresh token
/// is opaque, stored only as a SHA-256 hash, and replaced on every use;
/// presenting an already-used one revokes the whole family, which is what makes
/// a stolen token <em>detectable</em> rather than merely usable.</item>
/// </list>
/// </remarks>
public sealed class AuthService(
    IUserRepository users,
    IAuditRepository audit,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    ITokenService tokens,
    IUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger)
{
    private readonly AuthOptions _options = options.Value;

    public async Task<TokenPair> SignInAsync(
        string email,
        string password,
        string? device,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindCredentialsByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            // Spend roughly the same time a real verify would, so response time
            // does not reveal whether the address exists.
            passwordHasher.DummyVerify(password);
            Log.SignInFailed(logger, "unknown email", requestContext.IpAddress);

            throw new AuthenticationException();
        }

        if (user.LockedUntil is { } lockedUntil && lockedUntil > clock.Now)
        {
            Log.SignInFailed(logger, "account locked", requestContext.IpAddress);

            throw new AuthenticationException();
        }

        if (user.Status != UserStatus.Active)
        {
            Log.SignInFailed(logger, $"account {user.Status}", requestContext.IpAddress);

            throw new AuthenticationException();
        }

        if (!passwordHasher.Verify(password, user.PasswordHash))
        {
            var attempts = await users.RecordFailedLoginAsync(
                user.Id,
                _options.LockThreshold,
                _options.LockMinutes,
                cancellationToken);

            Log.SignInFailed(logger, "wrong password", requestContext.IpAddress);

            if (attempts >= _options.LockThreshold)
            {
                Log.AccountLocked(logger, user.Id, attempts);
            }

            audit.Write(
                user.Id,
                "auth.login_failed",
                nameof(User),
                user.Id.ToString(),
                before: null,
                after: new { FailedAttempts = attempts },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw new AuthenticationException();
        }

        // Correct password. Re-hash if the configured cost has moved on.
        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            await users.UpdatePasswordHashAsync(user.Id, passwordHasher.Hash(password), cancellationToken);
        }

        var principal = await users.FindPrincipalAsync(user.Id, cancellationToken);

        if (principal is null)
        {
            // Active credentials but no resolvable principal means the account
            // has no role profile row. That is a data problem, not the caller's,
            // and it must not produce a half-authenticated session.
            Log.SignInFailed(logger, "no principal for active user", requestContext.IpAddress);

            throw new AuthenticationException();
        }

        await users.RecordSuccessfulLoginAsync(principal.UserId, cancellationToken);

        audit.Write(
            principal.UserId,
            "auth.login",
            nameof(User),
            principal.UserId.ToString(),
            before: null,
            after: new { principal.Role },
            requestContext.IpAddress,
            requestContext.UserAgent);

        var pair = await IssueTokenPairAsync(principal, device, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.SignInSucceeded(logger, principal.UserId, principal.Role);

        return pair;
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating the refresh token.
    /// </summary>
    public async Task<TokenPair> RefreshAsync(
        string refreshToken,
        string? device,
        CancellationToken cancellationToken = default)
    {
        const string CouldNotRenew = "That session could not be renewed. Please sign in again.";

        var hash = tokens.HashRefreshToken(refreshToken);
        var stored = await refreshTokens.FindByHashAsync(hash, cancellationToken);

        if (stored is null)
        {
            throw new AuthenticationException(CouldNotRenew);
        }

        if (stored.RevokedAt is not null)
        {
            // A revoked token being presented means either a replay or a stolen
            // token. Either way every live session for this user is suspect, so
            // all of them are revoked and the user must sign in again.
            await refreshTokens.RevokeAllForUserAsync(stored.UserId, clock.Now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            Log.RefreshTokenReuse(logger, stored.UserId);

            throw new AuthenticationException(CouldNotRenew);
        }

        if (stored.ExpiresAt <= clock.Now)
        {
            throw new AuthenticationException("That session has expired. Please sign in again.");
        }

        var principal = await users.FindPrincipalAsync(stored.UserId, cancellationToken)
            ?? throw new AuthenticationException(CouldNotRenew);

        return await unitOfWork.TransactionAsync(async () =>
        {
            stored.RevokedAt = clock.Now;

            var pair = await IssueTokenPairAsync(principal, device, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return pair;
        }, cancellationToken);
    }

    public async Task SignOutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = tokens.HashRefreshToken(refreshToken);

        await refreshTokens.RevokeByHashAsync(hash, clock.Now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SignOutEverywhereAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await refreshTokens.RevokeAllForUserAsync(userId, clock.Now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Changes a password, requiring the current one.
    /// </summary>
    /// <remarks>
    /// The current password is required even though the caller is already
    /// authenticated: an access token in the wrong hands should not be enough to
    /// take the account over permanently.
    /// </remarks>
    public async Task ChangePasswordAsync(
        AuthenticatedUser user,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var stored = await users.FindCredentialsByEmailAsync(user.Email, cancellationToken);

        if (stored is null || !passwordHasher.Verify(currentPassword, stored.PasswordHash))
        {
            Log.SignInFailed(logger, "password change with wrong current password", requestContext.IpAddress);

            throw new AuthenticationException("Your current password is not correct.");
        }

        // Also revokes every refresh token for this user.
        await users.UpdatePasswordHashAsync(user.UserId, passwordHasher.Hash(newPassword), cancellationToken);

        audit.Write(
            user.UserId,
            "auth.password_changed",
            nameof(User),
            user.UserId.ToString(),
            before: null,
            after: null,
            requestContext.IpAddress,
            requestContext.UserAgent);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.PasswordChanged(logger, user.UserId);
    }

    private async Task<TokenPair> IssueTokenPairAsync(
        AuthenticatedUser principal,
        string? device,
        CancellationToken cancellationToken)
    {
        var access = tokens.IssueAccessToken(principal.UserId, principal.Role, _options.AccessTokenLifetime);
        var (refresh, refreshHash) = tokens.IssueRefreshToken();

        refreshTokens.Add(new RefreshToken
        {
            UserId = principal.UserId,
            TokenHash = refreshHash,
            Device = device is null ? null : device[..Math.Min(device.Length, 120)],
            ExpiresAt = clock.Now.Add(_options.RefreshTokenLifetime),
        });

        await Task.CompletedTask;

        return new TokenPair(
            access,
            refresh,
            (int)_options.AccessTokenLifetime.TotalSeconds,
            principal);
    }
}

public sealed record TokenPair(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    AuthenticatedUser User);

/// <summary>Refresh-token storage. Small enough to live beside the auth service.</summary>
public interface IRefreshTokenRepository
{
    void Add(RefreshToken token);

    Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken cancellationToken = default);

    Task RevokeByHashAsync(string hash, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<int> RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

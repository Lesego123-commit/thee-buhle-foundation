// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>
/// Refresh tokens.
/// </summary>
/// <remarks>
/// <para>
/// Every method here works on a <em>hash</em>. The token value the client holds
/// is never persisted, so a stolen database backup is not a set of working
/// sessions — which is the one property that makes a 30-day refresh token
/// tolerable at all.
/// </para>
/// <para>
/// Revoking is a timestamp, not a delete. A revoked token has to stay readable
/// for the reuse detection in <c>AuthService.RefreshAsync</c>: presenting a
/// token that has already been used is the signature of a stolen token, and it
/// can only be recognised if the used one is still on record. Deleting the row
/// would make a replayed token indistinguishable from a token that never
/// existed.
/// </para>
/// </remarks>
public sealed class RefreshTokenRepository(TheeBuhleDbContext context) : IRefreshTokenRepository
{
    public void Add(RefreshToken token) => context.RefreshTokens.Add(token);

    public Task<RefreshToken?> FindByHashAsync(
        string hash,
        CancellationToken cancellationToken = default) =>
        context.RefreshTokens.FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

    public async Task RevokeByHashAsync(
        string hash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await context.RefreshTokens
            .Where(token => token.TokenHash == hash && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.UpdatedAt, now),
                cancellationToken);

    /// <summary>
    /// Revokes every live token for one user.
    /// </summary>
    /// <remarks>
    /// Called on "sign out everywhere", on a password change, on a suspension,
    /// and — the interesting one — when a refresh token is <em>reused</em>. A
    /// replayed token means somebody has a copy, and there is no way to tell
    /// whether the genuine user or the thief is the one presenting it. So the
    /// whole family goes, both of them are signed out, and the real user signs in
    /// again with a password the thief does not have.
    /// </remarks>
    public Task<int> RevokeAllForUserAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.UpdatedAt, now),
                cancellationToken);
}

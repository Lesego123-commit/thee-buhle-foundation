// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>Users, credentials and the request principal.</summary>
public sealed class UserRepository(TheeBuhleDbContext context, IClock clock) : IUserRepository
{
    public Task<User?> FindCredentialsByEmailAsync(
        string email,
        CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

    /// <summary>
    /// Builds the request principal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rebuilt from the database on <strong>every request</strong> rather than
    /// read out of the JWT's claims. That costs one indexed query per request and
    /// buys the property that matters: a suspended account stops working
    /// immediately, instead of when its 15-minute access token happens to
    /// expire.
    /// </para>
    /// <para>
    /// It is also why the JWT carries only an id and a role. Putting the parent,
    /// learner and mentor ids in the token would make them a client-held copy of
    /// something that can change — and a parent whose guardian link was removed
    /// would keep the old one until the token expired.
    /// </para>
    /// <para>
    /// Returns null for anything other than an active account, which is how the
    /// middleware turns a suspension into a 401 without special-casing it.
    /// </para>
    /// </remarks>
    public async Task<AuthenticatedUser?> FindPrincipalAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.Status == UserStatus.Active)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.Role,

                // Each profile's parts are projected SEPARATELY rather than
                // concatenated in the query, and that is a bug fix rather than a
                // preference.
                //
                // The obvious version — FirstName + " " + Surname — looks like it
                // yields null when the profile is missing, so a ?? chain over the
                // four of them would pick whichever one exists. It does not: EF
                // translates string concatenation to
                // COALESCE(first,'') + ' ' + COALESCE(surname,''), so a missing
                // profile produces a single SPACE, which is not null. The ??
                // chain then stops at the first one and every user who is not a
                // parent ends up with an empty display name.
                //
                // Found by calling /api/auth/me as a mentor and seeing "".
                ParentId = (Guid?)user.ParentProfile!.Id,
                ParentFirst = user.ParentProfile!.FirstName,
                ParentSurname = user.ParentProfile!.Surname,

                LearnerId = (Guid?)user.LearnerProfile!.Id,
                LearnerFirst = user.LearnerProfile!.FirstName,
                LearnerSurname = user.LearnerProfile!.Surname,

                MentorId = (Guid?)user.MentorProfile!.Id,
                MentorFirst = user.MentorProfile!.FirstName,
                MentorSurname = user.MentorProfile!.Surname,

                StaffId = (Guid?)user.StaffProfile!.Id,
                StaffFirst = user.StaffProfile!.FirstName,
                StaffSurname = user.StaffProfile!.Surname,

                IsSafeguardingLead = (bool?)user.StaffProfile!.IsSafeguardingLead,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // Combined in C#, where null means null. A person is a parent or a
        // mentor or staff, not several — the unique index on each profile
        // table's user_id guarantees that — so at most one of these is set.
        var displayName = Name(row.ParentFirst, row.ParentSurname)
            ?? Name(row.LearnerFirst, row.LearnerSurname)
            ?? Name(row.MentorFirst, row.MentorSurname)
            ?? Name(row.StaffFirst, row.StaffSurname)
            ?? row.Email;

        // A staff member flagged as the safeguarding lead is promoted to the
        // SafeguardingLead role here rather than being stored with it. The flag
        // on the staff row is the single source of truth — storing the role
        // separately would give two places to change and one to forget, and the
        // thing being forgotten would be who may read child-safety reports.
        var role = row.IsSafeguardingLead == true && row.Role == Role.Staff
            ? Role.SafeguardingLead
            : row.Role;

        return new AuthenticatedUser(
            row.Id,
            row.Email,
            role,
            row.ParentId,
            row.LearnerId,
            row.MentorId,
            row.StaffId,
            displayName);
    }

    /// <summary>
    /// Joins a first name and surname, or null if neither is really there.
    /// </summary>
    /// <remarks>
    /// The whitespace check is the point: see the note in the projection above
    /// about EF turning a missing profile into a single space rather than a null.
    /// </remarks>
    private static string? Name(string? first, string? surname)
    {
        var joined = $"{first} {surname}".Trim();

        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    public async Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.FailedAttempts, 0)
                    .SetProperty(user => user.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(user => user.LastLoginAt, clock.Now)
                    .SetProperty(user => user.UpdatedAt, clock.Now),
                cancellationToken);

    /// <summary>
    /// Increments the failure counter and locks the account at the threshold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The increment and the lock decision happen in <strong>one UPDATE</strong>,
    /// computed in the database. The obvious implementation — read the count, add
    /// one, decide, write — has a race: two simultaneous guesses both read "4",
    /// both write "5", and the account is still unlocked after ten attempts.
    /// </para>
    /// <para>
    /// That is not a theoretical race. An attacker running a password list
    /// concurrently is exactly the situation the lock exists for, and it is
    /// precisely when the read-modify-write version fails.
    /// </para>
    /// </remarks>
    public async Task<int> RecordFailedLoginAsync(
        Guid userId,
        int lockThreshold,
        int lockMinutes,
        CancellationToken cancellationToken = default)
    {
        var lockedUntil = clock.Now.AddMinutes(lockMinutes);

        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.FailedAttempts, user => user.FailedAttempts + 1)
                    .SetProperty(
                        user => user.LockedUntil,
                        user => user.FailedAttempts + 1 >= lockThreshold ? lockedUntil : user.LockedUntil)
                    .SetProperty(user => user.UpdatedAt, clock.Now),
                cancellationToken);

        return await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.FailedAttempts)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the password hash and revokes every refresh token.
    /// </summary>
    /// <remarks>
    /// The revocation is not optional politeness. If the password was changed
    /// because it was compromised, an attacker holding a refresh token would
    /// otherwise keep a live session for the next thirty days — which makes the
    /// password change useless for the one case it most needs to work.
    /// </remarks>
    public async Task UpdatePasswordHashAsync(
        Guid userId,
        string hash,
        CancellationToken cancellationToken = default)
    {
        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.PasswordHash, hash)
                    .SetProperty(user => user.UpdatedAt, clock.Now),
                cancellationToken);

        await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(token => token.RevokedAt, clock.Now),
                cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        context.Users.AnyAsync(user => user.Email == email, cancellationToken);

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        await context.Users.AddAsync(user, cancellationToken);

        return user;
    }

    public async Task SetStatusAsync(
        Guid userId,
        UserStatus status,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(user => user.Status, status)
                    .SetProperty(user => user.UpdatedBy, actorId)
                    .SetProperty(user => user.UpdatedAt, clock.Now),
                cancellationToken);

        if (status != UserStatus.Active)
        {
            // Suspending an account must end its sessions. Without this the
            // person keeps working normally until their refresh token expires,
            // which for a dismissed volunteer is exactly the wrong outcome.
            await context.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(token => token.RevokedAt, clock.Now),
                    cancellationToken);
        }
    }

    /// <summary>
    /// The user ids in one audience.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolved by role through the profile tables rather than from a stored
    /// audience list, so an audience is always current — a parent who enrolled
    /// yesterday is in "parents" without anybody maintaining a list.
    /// </para>
    /// <para>
    /// Only active accounts. An announcement fanned out to a suspended account
    /// would create a delivery row nobody can ever read, which would make the
    /// "we told you" compliance record wrong in the direction that matters.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> IdsForAudienceAsync(
        Audience audience,
        CancellationToken cancellationToken = default)
    {
        var active = context.Users.AsNoTracking().Where(user => user.Status == UserStatus.Active);

        var query = audience switch
        {
            Audience.Parents => active.Where(user => user.Role == Role.Parent),
            Audience.Learners => active.Where(user => user.Role == Role.Learner),
            Audience.Mentors => active.Where(user => user.Role == Role.Mentor),
            // Spelled out rather than calling Role.IsStaffSide(), because an
            // extension method cannot be translated into SQL — EF would either
            // refuse the query or silently evaluate it in memory after loading
            // every user in the system.
            Audience.Staff => active.Where(user =>
                user.Role == Role.Staff || user.Role == Role.SafeguardingLead || user.Role == Role.Admin),
            Audience.All => active,
            _ => active.Where(_ => false),
        };

        return await query.Select(user => user.Id).ToListAsync(cancellationToken);
    }

    public async Task<UserContact?> FindContactAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.Status == UserStatus.Active)
            .Select(user => new UserContact(
                user.Id,
                user.Email,
                user.ParentProfile != null
                    ? user.ParentProfile.FirstName
                    : user.MentorProfile != null
                        ? user.MentorProfile.FirstName
                        : user.StaffProfile != null
                            ? user.StaffProfile.FirstName
                            : user.LearnerProfile != null
                                ? user.LearnerProfile.FirstName
                                : user.Email))
            .FirstOrDefaultAsync(cancellationToken);

    // ------------------------------------------------- escalation chain roles

    /// <summary>
    /// The mentor lead.
    /// </summary>
    /// <remarks>
    /// Matched on job title, because "mentor lead" is a job the foundation
    /// assigns rather than a system role with its own permissions — unlike the
    /// safeguarding lead, whose flag governs what they can read.
    /// </remarks>
    public Task<UserContact?> FindMentorLeadAsync(CancellationToken cancellationToken = default) =>
        FindStaffByTitleAsync("Mentor Lead", cancellationToken);

    public Task<UserContact?> FindProgrammeManagerAsync(CancellationToken cancellationToken = default) =>
        FindStaffByTitleAsync("Programme Manager", cancellationToken);

    /// <summary>
    /// The designated safeguarding lead.
    /// </summary>
    /// <remarks>
    /// Found by the <em>flag</em>, not by job title. The flag is what the
    /// filtered unique index enforces and what the visibility rules read, so
    /// resolving the recipient of a child-safety report any other way would allow
    /// the person who receives concerns and the person who may read them to be
    /// two different people.
    /// </remarks>
    public async Task<UserContact?> FindSafeguardingLeadAsync(CancellationToken cancellationToken = default) =>
        await context.Staff
            .AsNoTracking()
            .Where(staff => staff.IsSafeguardingLead && staff.User.Status == UserStatus.Active)
            .Select(staff => new UserContact(staff.UserId, staff.User.Email, staff.FirstName))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<UserContact?> FindStaffByTitleAsync(
        string jobTitle,
        CancellationToken cancellationToken) =>
        await context.Staff
            .AsNoTracking()
            .Where(staff => staff.JobTitle == jobTitle && staff.User.Status == UserStatus.Active)
            .Select(staff => new UserContact(staff.UserId, staff.User.Email, staff.FirstName))
            .FirstOrDefaultAsync(cancellationToken);
}

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>People, their accounts, and the one-time codes that let them in.</summary>
public sealed class PeopleRepository(TheeBuhleDbContext context, IClock clock) : IPeopleRepository
{
    public Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken = default) =>
        context.Users.AnyAsync(user => user.Email == email, cancellationToken);

    /// <summary>
    /// One more than the highest existing <c>A-</c> number.
    /// </summary>
    /// <remarks>
    /// Read, not counted: a learner who left keeps their reference, so a count
    /// would hand out a number that is already taken. Two staff members adding a
    /// learner in the same second would both get the same number, and the
    /// unique index on the reference refuses the second, which fails safe.
    /// </remarks>
    public async Task<string> NextLearnerReferenceAsync(CancellationToken cancellationToken = default)
    {
        var references = await context.Learners
            .AsNoTracking()
            .Where(learner => learner.ReferenceCode.StartsWith("A-"))
            .Select(learner => learner.ReferenceCode)
            .ToListAsync(cancellationToken);

        var highest = references
            .Select(reference => int.TryParse(reference.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .DefaultIfEmpty(100)
            .Max();

        return string.Create(CultureInfo.InvariantCulture, $"A-{highest + 1}");
    }

    public Task<bool> ProgrammeExistsAsync(Guid programmeId, CancellationToken cancellationToken = default) =>
        context.Programmes.AnyAsync(programme => programme.Id == programmeId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ExistingLearnersAsync(
        IReadOnlyCollection<Guid> learnerIds,
        CancellationToken cancellationToken = default) =>
        await context.Learners
            .Where(learner => learnerIds.Contains(learner.Id))
            .Select(learner => learner.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> SafeguardingLeadExistsAsync(CancellationToken cancellationToken = default) =>
        context.Staff.AnyAsync(staff => staff.IsSafeguardingLead, cancellationToken);

    public void AddUser(User user) => context.Users.Add(user);

    public void AddLearner(Learner learner, Enrolment? enrolment)
    {
        context.Learners.Add(learner);

        if (enrolment is not null)
        {
            context.Enrolments.Add(enrolment);
        }
    }

    public void AddParent(ParentGuardian parent, IEnumerable<GuardianLink> links)
    {
        context.ParentGuardians.Add(parent);
        context.GuardianLinks.AddRange(links);
    }

    public void AddMentor(Mentor mentor) => context.Mentors.Add(mentor);

    public void AddStaff(StaffMember staff) => context.Staff.Add(staff);

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<User?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<bool> IsSafeguardingLeadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Staff.AnyAsync(staff => staff.UserId == userId && staff.IsSafeguardingLead, cancellationToken);

    public async Task<string> DisplayNameAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var names = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                Staff = user.StaffProfile == null ? null : user.StaffProfile.FirstName + " " + user.StaffProfile.Surname,
                Parent = user.ParentProfile == null ? null : user.ParentProfile.FirstName + " " + user.ParentProfile.Surname,
                Mentor = user.MentorProfile == null ? null : user.MentorProfile.FirstName + " " + user.MentorProfile.Surname,
                Learner = user.LearnerProfile == null ? null : user.LearnerProfile.FirstName + " " + user.LearnerProfile.Surname,
                user.Email,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return names is null ? string.Empty : names.Staff ?? names.Parent ?? names.Mentor ?? names.Learner ?? names.Email;
    }

    public void AddCode(AccountCode code) => context.AccountCodes.Add(code);

    public Task RevokeOpenCodesAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        context.AccountCodes
            .Where(code => code.UserId == userId && code.UsedAt == null && code.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(code => code.RevokedAt, now), cancellationToken);

    public Task<AccountCode?> LatestUnusedCodeAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.AccountCodes
            .Where(code => code.UserId == userId && code.UsedAt == null && code.RevokedAt == null)
            .OrderByDescending(code => code.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<PersonView>> SearchAsync(
        Role? role,
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;

        var accounts = context.Users.AsNoTracking();

        if (role is { } wanted)
        {
            accounts = accounts.Where(user => user.Role == wanted);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = $"%{query.Trim()}%";

            accounts = accounts.Where(user =>
                EF.Functions.Like(user.Email, term)
                || (user.StaffProfile != null && (EF.Functions.Like(user.StaffProfile.FirstName, term) || EF.Functions.Like(user.StaffProfile.Surname, term)))
                || (user.ParentProfile != null && (EF.Functions.Like(user.ParentProfile.FirstName, term) || EF.Functions.Like(user.ParentProfile.Surname, term)))
                || (user.MentorProfile != null && (EF.Functions.Like(user.MentorProfile.FirstName, term) || EF.Functions.Like(user.MentorProfile.Surname, term)))
                || (user.LearnerProfile != null && (EF.Functions.Like(user.LearnerProfile.FirstName, term)
                    || EF.Functions.Like(user.LearnerProfile.Surname, term)
                    || EF.Functions.Like(user.LearnerProfile.ReferenceCode, term))));
        }

        var total = await accounts.CountAsync(cancellationToken);

        var rows = await accounts
            // Waiting invitations first: they are the ones staff come here to chase.
            .OrderBy(user => user.Status == UserStatus.Invited ? 0 : 1)
            .ThenByDescending(user => user.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.Role,
                user.Status,
                user.LastLoginAt,
                Staff = user.StaffProfile == null ? null : new { Name = user.StaffProfile.FirstName + " " + user.StaffProfile.Surname, user.StaffProfile.JobTitle, user.StaffProfile.IsSafeguardingLead },
                Parent = user.ParentProfile == null ? null : new
                {
                    Name = user.ParentProfile.FirstName + " " + user.ParentProfile.Surname,
                    Children = user.ParentProfile.GuardianLinks.Select(link => link.Learner.FirstName + " (" + link.Learner.ReferenceCode + ")").ToList(),
                },
                Mentor = user.MentorProfile == null ? null : new { Name = user.MentorProfile.FirstName + " " + user.MentorProfile.Surname, user.MentorProfile.ClearanceStatus },
                Learner = user.LearnerProfile == null ? null : new { Name = user.LearnerProfile.FirstName + " " + user.LearnerProfile.Surname, user.LearnerProfile.ReferenceCode, user.LearnerProfile.Grade },
                CodeExpiresAt = context.AccountCodes
                    .Where(code => code.UserId == user.Id && code.UsedAt == null && code.RevokedAt == null
                        && code.ExpiresAt > now && code.FailedAttempts < AccountCode.MaxAttempts)
                    .OrderByDescending(code => code.CreatedAt)
                    .Select(code => (DateTimeOffset?)code.ExpiresAt)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new PersonView(
                row.Id,
                row.Staff?.Name ?? row.Parent?.Name ?? row.Mentor?.Name ?? row.Learner?.Name ?? row.Email,
                row.Email,
                row.Staff?.IsSafeguardingLead == true ? Role.SafeguardingLead : row.Role,
                row.Status,
                row.Staff is not null ? row.Staff.JobTitle
                    : row.Parent is not null ? (row.Parent.Children.Count == 0 ? "No learner linked" : "Parent of " + string.Join(", ", row.Parent.Children))
                    : row.Mentor is not null ? "Clearance: " + row.Mentor.ClearanceStatus
                    : row.Learner is not null ? string.Create(CultureInfo.InvariantCulture, $"{row.Learner.ReferenceCode} · Grade {row.Learner.Grade}")
                    : null,
                row.LastLoginAt,
                row.CodeExpiresAt))
            .ToList();

        return new PagedResult<PersonView>(items, total, page, pageSize);
    }
}

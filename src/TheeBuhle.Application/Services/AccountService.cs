// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;

namespace TheeBuhle.Application.Services;

/// <summary>
/// Adding people, and letting them in: invitations and password resets.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no public sign-up, on purpose.</strong> An account here is
/// a window onto a child's marks, attendance and mentor notes. Open
/// registration would let anybody claim to be somebody's parent. So staff
/// create the person, link a parent to their child, and hand over a one-time
/// code. The person redeems it and chooses their own password, which means no
/// member of staff ever knows it.
/// </para>
/// <para>
/// A forgotten password works the same way: staff issue a reset code after
/// confirming who is asking. There is no "email me a reset link" button, because
/// that would let anybody who learns a parent's email address start a takeover,
/// and the foundation's email is not set up yet anyway.
/// </para>
/// <para>
/// Who may issue a code to whom is the security boundary. Programme staff may
/// invite and reset learners, parents and mentors. Only an administrator may do
/// so for a staff-side account, or programme staff could reset the
/// safeguarding lead's password and read the safeguarding queue.
/// </para>
/// </remarks>
public sealed class AccountService(
    IPeopleRepository people,
    IUserRepository users,
    IAuditRepository audit,
    IPasswordHasher passwordHasher,
    IEmailSender email,
    IUnitOfWork unitOfWork,
    IRequestContext requestContext,
    IClock clock,
    ILogger<AccountService> logger)
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    public static readonly TimeSpan ResetLifetime = TimeSpan.FromDays(2);

    /// <summary>The message for every failed redemption, whatever the reason.</summary>
    /// <remarks>
    /// One sentence for an unknown email, a wrong code, an expired code and a
    /// used one. Anything more specific would tell a guesser which half of
    /// their guess was right.
    /// </remarks>
    public const string RedeemFailed =
        "That code is not valid for this email address, or it has expired. Ask the foundation for a new one.";

    // ------------------------------------------------------------- creating

    public async Task<CreatedPerson> CreateLearnerAsync(
        AuthenticatedUser actor,
        NewLearner request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProgrammeId is { } programmeId
            && !await people.ProgrammeExistsAsync(programmeId, cancellationToken))
        {
            throw new DomainRuleException("That programme does not exist.");
        }

        // A login is optional: a Grade 8 learner may have no phone or email,
        // and their parent follows their progress instead.
        User? user = null;

        if (NormaliseEmail(request.Email) is { } address)
        {
            user = await NewAccountAsync(address, Role.Learner, cancellationToken);
        }

        var learner = new Learner
        {
            ReferenceCode = await people.NextLearnerReferenceAsync(cancellationToken),
            FirstName = request.FirstName.Trim(),
            Surname = request.Surname.Trim(),
            DateOfBirth = request.DateOfBirth,
            Grade = request.Grade,
            School = Tidy(request.School),
            Suburb = Tidy(request.Suburb),
            User = user,
        };

        var enrolment = request.ProgrammeId is { } programme
            ? new Enrolment
            {
                Learner = learner,
                ProgrammeId = programme,
                Term = CurrentTerm(clock.Today),
                Status = EnrolmentStatus.Active,
            }
            : null;

        people.AddLearner(learner, enrolment);

        return await FinishAsync(actor, user, learner.Id, learner.ReferenceCode, $"{learner.FirstName} {learner.Surname}", cancellationToken);
    }

    public async Task<CreatedPerson> CreateParentAsync(
        AuthenticatedUser actor,
        NewParent request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var learnerIds = request.LearnerIds.Distinct().ToList();

        // A parent account with no child is an account that can see nothing,
        // and one linked to a learner who does not exist is a mistake that
        // would surface as an empty app. Both are refused here.
        if (learnerIds.Count == 0)
        {
            throw new DomainRuleException("Link the parent to at least one learner.");
        }

        var existing = await people.ExistingLearnersAsync(learnerIds, cancellationToken);

        if (existing.Count != learnerIds.Count)
        {
            throw new DomainRuleException("One of those learners does not exist.");
        }

        var user = await NewAccountAsync(
            NormaliseEmail(request.Email) ?? throw new DomainRuleException("A parent needs an email address to sign in with."),
            Role.Parent,
            cancellationToken);

        var parent = new ParentGuardian
        {
            User = user,
            FirstName = request.FirstName.Trim(),
            Surname = request.Surname.Trim(),
            Phone = request.Phone.Trim(),
            Relationship = Tidy(request.Relationship) ?? "guardian",
            ContactConfirmedAt = clock.Today,
        };

        people.AddParent(parent, learnerIds.Select((learnerId, index) => new GuardianLink
        {
            Parent = parent,
            LearnerId = learnerId,
            // The first learner named is the primary link: the child whose
            // panel the app opens on.
            IsPrimary = index == 0,
            CreatedAt = clock.Now,
        }));

        return await FinishAsync(actor, user, parent.Id, null, $"{parent.FirstName} {parent.Surname}", cancellationToken);
    }

    public async Task<CreatedPerson> CreateMentorAsync(
        AuthenticatedUser actor,
        NewMentor request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        // Recording a clearance is recording a fact about a police check. An
        // expiry in the past is not a clearance, and saying so here is better
        // than a mentor who appears cleared and cannot be assigned.
        if (request.ClearedUntil is { } expiry && expiry <= clock.Today)
        {
            throw new DomainRuleException("The police clearance has already expired.");
        }

        var user = await NewAccountAsync(
            NormaliseEmail(request.Email) ?? throw new DomainRuleException("A mentor needs an email address to sign in with."),
            Role.Mentor,
            cancellationToken);

        var mentor = new Mentor
        {
            User = user,
            FirstName = request.FirstName.Trim(),
            Surname = request.Surname.Trim(),
            Occupation = Tidy(request.Occupation),
            ClearanceStatus = request.ClearedUntil is null ? ClearanceStatus.Pending : ClearanceStatus.Cleared,
            ClearanceExpiry = request.ClearedUntil,
        };

        people.AddMentor(mentor);

        return await FinishAsync(actor, user, mentor.Id, null, $"{mentor.FirstName} {mentor.Surname}", cancellationToken);
    }

    /// <summary>A staff account. The endpoint allows administrators only.</summary>
    public async Task<CreatedPerson> CreateStaffAsync(
        AuthenticatedUser actor,
        NewStaff request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (actor.Role != Role.Admin)
        {
            throw new AccessDeniedException("Only an administrator can add staff.");
        }

        // The database refuses a second lead with a filtered unique index; this
        // says so in words before it gets that far.
        if (request.IsSafeguardingLead && await people.SafeguardingLeadExistsAsync(cancellationToken))
        {
            throw new DomainRuleException(
                "There is already a designated safeguarding lead. Only one person can hold that role.");
        }

        var user = await NewAccountAsync(
            NormaliseEmail(request.Email) ?? throw new DomainRuleException("A staff member needs an email address."),
            request.IsAdministrator ? Role.Admin : Role.Staff,
            cancellationToken);

        var staff = new StaffMember
        {
            User = user,
            FirstName = request.FirstName.Trim(),
            Surname = request.Surname.Trim(),
            JobTitle = request.JobTitle.Trim(),
            IsSafeguardingLead = request.IsSafeguardingLead,
        };

        people.AddStaff(staff);

        return await FinishAsync(actor, user, staff.Id, null, $"{staff.FirstName} {staff.Surname}", cancellationToken);
    }

    // ---------------------------------------------------------------- codes

    /// <summary>
    /// A new code for an existing account: an invitation if they have never
    /// signed in, a password reset if they have.
    /// </summary>
    /// <remarks>
    /// Any earlier unused code for the same person stops working, so a code
    /// sent to the wrong number can be cancelled by issuing another.
    /// </remarks>
    public async Task<IssuedCode> IssueCodeAsync(
        AuthenticatedUser actor,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var user = await people.FindUserAsync(userId, cancellationToken)
            ?? throw new DomainRuleException("That account does not exist.");

        await AssertMayIssueForAsync(actor, user, cancellationToken);

        var purpose = user.Status switch
        {
            UserStatus.Invited => AccountCodePurpose.Invite,
            UserStatus.Active => AccountCodePurpose.PasswordReset,

            // A suspended account is suspended for a reason. A reset code would
            // be a way back in that skips whoever decided that.
            _ => throw new DomainRuleException(
                $"This account is {user.Status.ToString().ToLowerInvariant()}. Reactivate it before issuing a code."),
        };

        await people.RevokeOpenCodesAsync(user.Id, clock.Now, cancellationToken);

        var issued = Issue(user, purpose, actor.UserId);

        audit.Write(
            actor.UserId,
            purpose == AccountCodePurpose.Invite ? "people.invite_reissued" : "auth.reset_code_issued",
            nameof(User),
            user.Id.ToString(),
            before: null,
            after: new { Purpose = purpose.ToString(), issued.ExpiresAt },
            requestContext.IpAddress,
            requestContext.UserAgent);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        Log.AccountCodeIssued(logger, purpose, user.Id, actor.UserId);

        var name = await people.DisplayNameAsync(user.Id, cancellationToken);

        return issued with { Emailed = await TryEmailAsync(user.Email, name, issued, cancellationToken) };
    }

    /// <summary>
    /// Redeems a code: sets the password, activates an invited account, and
    /// signs every existing session out.
    /// </summary>
    public async Task<AccountCodePurpose> RedeemAsync(
        string emailAddress,
        string code,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newPassword);

        if (newPassword.Length < 12)
        {
            throw new DomainRuleException("Choose a password of at least 12 characters.");
        }

        var address = NormaliseEmail(emailAddress);
        var user = address is null ? null : await people.FindUserByEmailAsync(address, cancellationToken);

        if (user is null)
        {
            // Roughly the time a real check takes, so the response time does
            // not reveal whether the address has an account.
            passwordHasher.DummyVerify(newPassword);

            throw Refused("unknown email");
        }

        var stored = await people.LatestUnusedCodeAsync(user.Id, cancellationToken);

        if (stored is null || !stored.IsOpen(clock.Now))
        {
            throw Refused(stored is null ? "no code" : "code expired or spent");
        }

        if (!AccountCodes.Matches(code, stored.CodeHash))
        {
            stored.FailedAttempts++;

            audit.Write(
                user.Id,
                "auth.code_failed",
                nameof(User),
                user.Id.ToString(),
                before: null,
                after: new { stored.FailedAttempts },
                requestContext.IpAddress,
                requestContext.UserAgent);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw Refused("wrong code");
        }

        // An invitation is only for an account nobody has signed in to yet, and
        // a reset only for an active one. A code issued before the account was
        // suspended must not reactivate it.
        var expected = stored.Purpose == AccountCodePurpose.Invite ? UserStatus.Invited : UserStatus.Active;

        if (user.Status != expected)
        {
            throw Refused($"account is {user.Status}");
        }

        stored.UsedAt = clock.Now;
        user.Status = UserStatus.Active;
        user.FailedAttempts = 0;
        user.LockedUntil = null;

        audit.Write(
            user.Id,
            stored.Purpose == AccountCodePurpose.Invite ? "auth.invite_accepted" : "auth.password_reset",
            nameof(User),
            user.Id.ToString(),
            before: null,
            after: null,
            requestContext.IpAddress,
            requestContext.UserAgent);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the save, because it is a direct update. It also revokes every
        // refresh token, which is the point of a reset: whoever had the old
        // password is signed out everywhere.
        await users.UpdatePasswordHashAsync(user.Id, passwordHasher.Hash(newPassword), cancellationToken);

        Log.AccountCodeRedeemed(logger, stored.Purpose, user.Id);

        return stored.Purpose;
    }

    // -------------------------------------------------------------- helpers

    private async Task<User> NewAccountAsync(string address, Role role, CancellationToken cancellationToken)
    {
        if (await people.EmailInUseAsync(address, cancellationToken))
        {
            throw new DomainRuleException($"{address} already has an account.");
        }

        var user = new User
        {
            Email = address,
            // A hash of 32 random bytes nobody is told. The account cannot be
            // signed in to until the invitation sets a real password, and the
            // status check refuses it before this is ever compared anyway.
            PasswordHash = passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            Role = role,
            Status = UserStatus.Invited,
        };

        people.AddUser(user);

        return user;
    }

    private async Task<CreatedPerson> FinishAsync(
        AuthenticatedUser actor,
        User? user,
        Guid profileId,
        string? referenceCode,
        string name,
        CancellationToken cancellationToken)
    {
        var issued = user is null ? null : Issue(user, AccountCodePurpose.Invite, actor.UserId);

        audit.Write(
            actor.UserId,
            "people.created",
            user is null ? nameof(Learner) : nameof(User),
            (user?.Id ?? profileId).ToString(),
            before: null,
            after: new { Role = user?.Role.ToString() ?? nameof(Role.Learner), Name = name, referenceCode, HasLogin = user is not null },
            requestContext.IpAddress,
            requestContext.UserAgent);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (user is not null)
        {
            Log.AccountCreated(logger, user.Role, user.Id, actor.UserId);
        }

        var emailed = issued is not null && user is not null
            && await TryEmailAsync(user.Email, name, issued, cancellationToken);

        return new CreatedPerson(user?.Id, profileId, referenceCode, issued is null ? null : issued with { Emailed = emailed });
    }

    private IssuedCode Issue(User user, AccountCodePurpose purpose, Guid issuedBy)
    {
        var code = AccountCodes.Generate();
        var expiresAt = clock.Now.Add(purpose == AccountCodePurpose.Invite ? InviteLifetime : ResetLifetime);

        people.AddCode(new AccountCode
        {
            User = user,
            Purpose = purpose,
            CodeHash = AccountCodes.Hash(code),
            ExpiresAt = expiresAt,
            IssuedBy = issuedBy,
        });

        return new IssuedCode(code, purpose, expiresAt, user.Email, Emailed: false);
    }

    private async Task AssertMayIssueForAsync(AuthenticatedUser actor, User target, CancellationToken cancellationToken)
    {
        if (actor.Role == Role.Admin)
        {
            return;
        }

        var targetIsStaffSide = target.Role.IsStaffSide()
            || await people.IsSafeguardingLeadAsync(target.Id, cancellationToken);

        if (targetIsStaffSide)
        {
            throw new AccessDeniedException("Only an administrator can issue a code to a staff account.");
        }
    }

    /// <summary>
    /// Emails the code when the foundation's mail is configured. Never fails
    /// the request: staff are shown the code either way and can pass it on.
    /// </summary>
    private async Task<bool> TryEmailAsync(string address, string name, IssuedCode issued, CancellationToken cancellationToken)
    {
        if (!email.IsEnabled)
        {
            return false;
        }

        var invite = issued.Purpose == AccountCodePurpose.Invite;
        var expires = issued.ExpiresAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

        var text = (invite
                ? "You have been added to the Thee Buhle Foundation app."
                : "The foundation has reset your password on request.")
            + $"\n\nYour code: {issued.Code}\nIt works once, until {expires}.\n\n"
            + "Open the Thee Buhle app and tap \"I have a code\", or go to "
            + "https://theebuhlefoundation.co.za/activate, then enter your email address, this code and a new password.\n\n"
            + "If you did not expect this, ignore it and tell the foundation.";

        try
        {
            return await email.SendAsync(
                new EmailMessage(
                    address,
                    name,
                    invite ? "Your Thee Buhle Foundation invitation" : "Your Thee Buhle Foundation reset code",
                    $"<p>{WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal)}</p>",
                    text),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Advisory, as with the Section 18A certificates: a mail outage must
            // not undo an account that was created correctly.
            return false;
        }
    }

    private DomainRuleException Refused(string reason)
    {
        Log.AccountCodeRefused(logger, reason, requestContext.IpAddress);

        return new DomainRuleException(RedeemFailed);
    }

    private static string? NormaliseEmail(string? address) =>
        string.IsNullOrWhiteSpace(address) ? null : address.Trim().ToLowerInvariant();

    private static string? Tidy(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The school term, as enrolments store it: <c>2026-3</c>.</summary>
    /// <remarks>South African public schools run four terms, roughly one a quarter.</remarks>
    public static string CurrentTerm(DateOnly today) =>
        string.Create(CultureInfo.InvariantCulture, $"{today.Year}-{((today.Month - 1) / 3) + 1}");
}

/// <summary>Generating, normalising and checking one-time account codes.</summary>
public static class AccountCodes
{
    /// <summary>
    /// No 0/O, 1/I/L: a code read out over the phone or copied from an SMS must
    /// not have characters people mistake for each other.
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public const int Length = 10;

    /// <summary>A new code, formatted for reading: <c>K7MPQ-2XRT9</c>.</summary>
    /// <remarks>
    /// Ten characters from a 31-character alphabet is about 49 bits. Five wrong
    /// guesses kill a code and it expires within a week, so online guessing is
    /// hopeless, and only the hash is stored.
    /// </remarks>
    public static string Generate()
    {
        var raw = RandomNumberGenerator.GetString(Alphabet, Length);

        return $"{raw[..5]}-{raw[5..]}";
    }

    /// <summary>Upper case, with the dash and any spaces a person typed removed.</summary>
    public static string Normalise(string? code) =>
        new((code ?? string.Empty).ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    public static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(code))));

    /// <summary>Compares in constant time, so the check does not leak how much of a guess was right.</summary>
    public static bool Matches(string? candidate, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(candidate ?? string.Empty)),
            Encoding.ASCII.GetBytes(storedHash));
}

public sealed record NewLearner(
    string FirstName,
    string Surname,
    DateOnly DateOfBirth,
    int Grade,
    string? School,
    string? Suburb,
    Guid? ProgrammeId,
    string? Email);

public sealed record NewParent(
    string FirstName,
    string Surname,
    string Email,
    string Phone,
    string? Relationship,
    IReadOnlyList<Guid> LearnerIds);

public sealed record NewMentor(
    string FirstName,
    string Surname,
    string Email,
    string? Occupation,
    DateOnly? ClearedUntil);

public sealed record NewStaff(
    string FirstName,
    string Surname,
    string Email,
    string JobTitle,
    bool IsSafeguardingLead,
    bool IsAdministrator);

/// <summary>A code, shown to the staff member once and never stored in readable form.</summary>
public sealed record IssuedCode(
    string Code,
    AccountCodePurpose Purpose,
    DateTimeOffset ExpiresAt,
    string Email,
    bool Emailed);

public sealed record CreatedPerson(
    Guid? UserId,
    Guid ProfileId,
    string? ReferenceCode,
    IssuedCode? Invite);

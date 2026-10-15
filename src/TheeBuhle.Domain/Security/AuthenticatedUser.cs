// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using TheeBuhle.Domain.Common;

namespace TheeBuhle.Domain.Security;

/// <summary>
/// The authenticated principal for one request.
/// </summary>
/// <remarks>
/// <para>
/// Carries the role <em>and</em> the profile id for that role, because
/// authorisation in this system is almost never "is this a parent?" — it is "is
/// this parent a guardian of that learner?". Holding
/// <see cref="ParentId"/> / <see cref="MentorId"/> / <see cref="LearnerId"/>
/// here means the access policy can answer that without a second lookup on
/// every call.
/// </para>
/// <para>
/// Built fresh from the database on every request rather than read from the
/// token's claims. The token proves who the caller is; it does not prove what
/// they may still do. A staff member suspended two minutes ago holds a valid,
/// unexpired JWT — and the principal lookup only returns active accounts, so
/// that token stops working immediately. Trusting the role claim instead would
/// leave them with up to fifteen minutes of access after being suspended.
/// </para>
/// </remarks>
public sealed record AuthenticatedUser(
    Guid UserId,
    string Email,
    Role Role,
    Guid? ParentId = null,
    Guid? LearnerId = null,
    Guid? MentorId = null,
    Guid? StaffId = null,
    string DisplayName = "")
{
    public bool Is(params Role[] roles) => roles.Contains(Role);

    public bool IsStaffSide => Role.IsStaffSide();

    public bool MayReadFlaggedConcerns => Role.MayReadFlaggedConcerns();

    public string RoleLabel => Role.Label();
}

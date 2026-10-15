// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// Adding people and letting them in: the staff console's People section.
/// </summary>
/// <remarks>
/// <para>
/// There is no public sign-up. Staff create a learner, parent, mentor or staff
/// member here, and each person with a login gets a one-time invite code. They
/// redeem it at <c>POST /api/auth/redeem-code</c> (the app's "I have a code"
/// screen, or <c>/activate</c> on the website) and choose their own password.
/// </para>
/// <para>
/// The code is in the response exactly once. Only its hash is stored, so a
/// lost code cannot be looked up again; issuing a new one cancels the old.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/people")]
[Produces("application/json")]
[RequireStaff]
public sealed class PeopleController(
    AccountService accounts,
    IPeopleRepository people,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Accounts, waiting invitations first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PersonView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PersonView>>> Search(
        [FromQuery] Role? role,
        [FromQuery] [MaxLength(80)] string? query,
        [FromQuery] [Range(1, 1000)] int page = 1,
        [FromQuery] [Range(1, 100)] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await people.SearchAsync(role, query, page, pageSize, cancellationToken));

    /// <summary>Adds a learner, optionally with their own login, and enrols them.</summary>
    [HttpPost("learners")]
    [ProducesResponseType(typeof(CreatedPerson), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedPerson>> AddLearner(
        [FromBody] AddLearnerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await accounts.CreateLearnerAsync(
            currentUser.Require(),
            new NewLearner(
                request.FirstName,
                request.Surname,
                request.DateOfBirth,
                request.Grade,
                request.School,
                request.Suburb,
                request.ProgrammeId,
                request.Email),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Adds a parent or guardian, linked to their children.</summary>
    [HttpPost("parents")]
    [ProducesResponseType(typeof(CreatedPerson), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedPerson>> AddParent(
        [FromBody] AddParentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await accounts.CreateParentAsync(
            currentUser.Require(),
            new NewParent(
                request.FirstName,
                request.Surname,
                request.Email,
                request.Phone,
                request.Relationship,
                request.LearnerIds),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Adds a mentor, normally a volunteer whose screening is complete.</summary>
    [HttpPost("mentors")]
    [ProducesResponseType(typeof(CreatedPerson), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedPerson>> AddMentor(
        [FromBody] AddMentorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await accounts.CreateMentorAsync(
            currentUser.Require(),
            new NewMentor(request.FirstName, request.Surname, request.Email, request.Occupation, request.ClearedUntil),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Adds a staff member. Administrators only.</summary>
    [HttpPost("staff")]
    [RequireUser(Role.Admin)]
    [ProducesResponseType(typeof(CreatedPerson), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedPerson>> AddStaff(
        [FromBody] AddStaffRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await accounts.CreateStaffAsync(
            currentUser.Require(),
            new NewStaff(
                request.FirstName,
                request.Surname,
                request.Email,
                request.JobTitle,
                request.IsSafeguardingLead,
                request.IsAdministrator),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// A new code: an invitation for someone who has never signed in, or a
    /// password reset for someone who has.
    /// </summary>
    /// <response code="403">Programme staff asking for a code for a staff account.</response>
    /// <response code="409">The account is suspended or deactivated.</response>
    [HttpPost("{userId:guid}/code")]
    [ProducesResponseType(typeof(IssuedCode), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IssuedCode>> IssueCode(Guid userId, CancellationToken cancellationToken) =>
        Ok(await accounts.IssueCodeAsync(currentUser.Require(), userId, cancellationToken));
}

public sealed record AddLearnerRequest
{
    [Required, MaxLength(80)] public required string FirstName { get; init; }

    [Required, MaxLength(80)] public required string Surname { get; init; }

    [Required] public DateOnly DateOfBirth { get; init; }

    [Range(1, 12)] public int Grade { get; init; }

    [MaxLength(120)] public string? School { get; init; }

    [MaxLength(80)] public string? Suburb { get; init; }

    /// <summary>Enrols the learner for the current term. Without it they do not appear on registers.</summary>
    public Guid? ProgrammeId { get; init; }

    /// <summary>Optional: only a learner old enough for their own login gets one.</summary>
    [EmailAddress, MaxLength(190)] public string? Email { get; init; }
}

public sealed record AddParentRequest
{
    [Required, MaxLength(80)] public required string FirstName { get; init; }

    [Required, MaxLength(80)] public required string Surname { get; init; }

    [Required, EmailAddress, MaxLength(190)] public required string Email { get; init; }

    [Required, MaxLength(30)] public required string Phone { get; init; }

    [MaxLength(40)] public string? Relationship { get; init; }

    [Required, MinLength(1)] public required IReadOnlyList<Guid> LearnerIds { get; init; }
}

public sealed record AddMentorRequest
{
    [Required, MaxLength(80)] public required string FirstName { get; init; }

    [Required, MaxLength(80)] public required string Surname { get; init; }

    [Required, EmailAddress, MaxLength(190)] public required string Email { get; init; }

    [MaxLength(120)] public string? Occupation { get; init; }

    /// <summary>The police clearance's expiry, once screening is complete. Empty means pending.</summary>
    public DateOnly? ClearedUntil { get; init; }
}

public sealed record AddStaffRequest
{
    [Required, MaxLength(80)] public required string FirstName { get; init; }

    [Required, MaxLength(80)] public required string Surname { get; init; }

    [Required, EmailAddress, MaxLength(190)] public required string Email { get; init; }

    [Required, MaxLength(120)] public required string JobTitle { get; init; }

    public bool IsSafeguardingLead { get; init; }

    public bool IsAdministrator { get; init; }
}

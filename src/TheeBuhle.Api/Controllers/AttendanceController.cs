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
/// Capturing a Saturday register.
/// </summary>
/// <remarks>
/// <para>
/// The bulk endpoint is the one that gets used. A staff member standing in a
/// hall in Sebokeng captures twenty-four learners at once on a phone, so the
/// design goal is one request rather than twenty-four — and, more importantly,
/// <strong>one transaction</strong>: a request that half-succeeds leaves a
/// register that is neither captured nor not.
/// </para>
/// <para>
/// Each learner marked present raises one domain event, which has three
/// consequences the caller never sees: any badge now due is awarded, the parent
/// is notified if the learner was absent or late, and the learner's cached
/// summary is refreshed. Two of those three are advisory, so a push provider
/// being down cannot make the register fail to save.
/// </para>
/// </remarks>
[ApiController]
[Route("api/attendance")]
[Produces("application/json")]
[RequireStaff]
public sealed class AttendanceController(
    AttendanceService attendance,
    ILearnerRepository learners,
    ISessionRepository sessions,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>The register for one session.</summary>
    /// <remarks>
    /// Every actively enrolled learner with any mark already captured joined on,
    /// so the console can render a part-completed register — which is the normal
    /// case, because a register is captured while the session is running.
    /// </remarks>
    [HttpGet("sessions/{sessionId:guid}/register")]
    [ProducesResponseType(typeof(IReadOnlyList<RegisterRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RegisterRow>>> Register(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (await sessions.FindAsync(sessionId, cancellationToken) is null)
        {
            return NotFound(new { detail = "That session does not exist." });
        }

        return Ok(await learners.RegisterForSessionAsync(sessionId, cancellationToken));
    }

    /// <summary>Recent sessions, and how complete each register is.</summary>
    /// <remarks>
    /// The console's landing view, and the <em>uncaptured</em> sessions are the
    /// point. A session whose register was never taken is invisible in an
    /// attendance report — it looks like a week on which nobody was absent, which
    /// quietly inflates every figure the foundation publishes.
    /// </remarks>
    [HttpGet("sessions/recent")]
    [ProducesResponseType(typeof(IReadOnlyList<RegisterState>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RegisterState>>> Recent(
        [FromQuery] [Range(1, 100)] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sessions.RecentWithRegisterStateAsync(limit, cancellationToken));

    /// <summary>Captures or amends one learner's mark.</summary>
    /// <remarks>
    /// Idempotent on (session, learner): capturing the same learner twice amends
    /// the mark rather than creating a second one, which is what makes a staff
    /// member correcting a mistyped mark safe. The response says which it was.
    /// </remarks>
    [HttpPost("sessions/{sessionId:guid}/entries")]
    [ProducesResponseType(typeof(CaptureResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CaptureResult>> Capture(
        Guid sessionId,
        [FromBody] CaptureEntryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await attendance.CaptureAsync(
            currentUser.Require(),
            sessionId,
            request.LearnerId,
            request.Status,
            request.ArrivedAt,
            request.Note,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Captures a whole register in one transaction.</summary>
    /// <remarks>
    /// <para>
    /// All or nothing. A register that saved eighteen of twenty-four marks is
    /// worse than one that saved none, because the eighteen look complete and the
    /// six missing learners are silently absent from every report.
    /// </para>
    /// <para>
    /// A duplicate learner in the payload is rejected rather than last-write-wins,
    /// because two different marks for one learner in one request means the
    /// client has a bug and guessing which one was meant would hide it.
    /// </para>
    /// </remarks>
    /// <response code="200">Captured, with how many were new and how many amended.</response>
    /// <response code="409">
    /// The session does not exist, a learner is not enrolled in its programme,
    /// the same learner appears twice, or the batch is larger than 400.
    /// </response>
    [HttpPost("sessions/{sessionId:guid}/register")]
    [ProducesResponseType(typeof(BulkCaptureResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BulkCaptureResult>> CaptureBulk(
        Guid sessionId,
        [FromBody] CaptureRegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entries = request.Entries
            .Select(entry => new RegisterEntry(
                entry.LearnerId,
                entry.Status,
                entry.ArrivedAt,
                entry.Note))
            .ToList();

        var result = await attendance.CaptureBulkAsync(
            currentUser.Require(),
            sessionId,
            entries,
            cancellationToken);

        return Ok(result);
    }
}

public sealed record CaptureEntryRequest
{
    [Required]
    public Guid LearnerId { get; init; }

    [Required]
    public AttendanceStatus Status { get; init; }

    /// <summary>
    /// When the learner arrived, for a late mark.
    /// </summary>
    /// <remarks>
    /// Clamped to the session's own window by the service. A phone with a wrong
    /// clock must not record a learner arriving at a session three hours before
    /// it started.
    /// </remarks>
    public DateTimeOffset? ArrivedAt { get; init; }

    [MaxLength(255)]
    public string? Note { get; init; }
}

public sealed record CaptureRegisterRequest
{
    /// <summary>
    /// One entry per learner.
    /// </summary>
    /// <remarks>
    /// Bounded at 400 by the service — larger than the biggest real session by a
    /// wide margin, and small enough that a runaway client cannot post a
    /// million-row transaction.
    /// </remarks>
    [Required]
    [MinLength(1)]
    public required IReadOnlyList<CaptureEntryRequest> Entries { get; init; }
}

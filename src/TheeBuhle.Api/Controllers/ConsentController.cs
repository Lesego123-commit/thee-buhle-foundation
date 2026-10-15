// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// Permission forms and the consent log.
/// </summary>
/// <remarks>
/// <para>
/// Note what this controller does <strong>not</strong> have: any way to change
/// or delete a consent record. The log is append-only — a withdrawal is a new
/// decision, never an edit of the granting one — and the absence of a PUT and a
/// DELETE is how that is expressed at the API boundary.
/// </para>
/// <para>
/// POPIA requires the full consent history to stay reconstructible. If a parent
/// asks "what did I agree to, and when did I change my mind?", the system has to
/// answer precisely, which it cannot do if a decision can be overwritten.
/// </para>
/// </remarks>
[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class ConsentController(
    ConsentService consent,
    ICommunicationRepository communications,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>The permission forms still awaiting a decision for one learner.</summary>
    /// <remarks>
    /// A form whose permission was <em>withdrawn</em> appears here again, because
    /// a withdrawal leaves it unanswered. Asking "has any consent row ever
    /// existed" instead would treat a withdrawal as an answer and hide the form
    /// — which is exactly the bug the append-only design exists to make visible.
    /// </remarks>
    [HttpGet("learners/{learnerId:guid}/forms")]
    [RequireUser]
    [ProducesResponseType(typeof(IReadOnlyList<OutstandingForm>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OutstandingForm>>> OutstandingForms(
        Guid learnerId,
        [FromServices] Application.Security.AccessPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!await policy.CanReadLearnerAsync(currentUser.Require(), learnerId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { detail = "You may not view that learner." });
        }

        return Ok(await communications.OutstandingFormsForAsync(learnerId, cancellationToken));
    }

    /// <summary>Records a guardian's decision on a permission form.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Only a registered guardian of the learner may call this — not
    /// staff, and not an administrator.</strong> A consent record a guardian did
    /// not create is worthless as evidence that they consented, so there is no
    /// override and no "on behalf of" parameter.
    /// </para>
    /// <para>
    /// What is recorded is a keyed hash over the facts: this guardian,
    /// authenticated, made this decision at this time from this address. That is
    /// better evidence than an image of a signature, which could have been copied
    /// from another form, and it can be verified years later by recomputing it.
    /// </para>
    /// <para>
    /// A closed form can still be <em>withdrawn</em> — a parent may change their
    /// mind about an event that has not happened yet — but it cannot be newly
    /// granted, because staff have already acted on the headcount.
    /// </para>
    /// </remarks>
    /// <response code="201">Recorded, with the signature reference.</response>
    /// <response code="409">
    /// Not a guardian of this learner; or there is no active permission to
    /// withdraw; or the same decision is already recorded; or the form has closed.
    /// </response>
    [HttpPost("forms/{formId:guid}/consent")]
    [RequireUser(Role.Parent)]
    [ProducesResponseType(typeof(ConsentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConsentResponse>> Record(
        Guid formId,
        [FromBody] RecordConsentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await consent.RecordAsync(
            currentUser.Require(),
            formId,
            request.LearnerId,
            request.Decision,
            cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new ConsentResponse(
                result.ConsentId,
                result.Decision,
                result.SignatureRef,
                result.SignedAt,
                result.Message));
    }

    /// <summary>The full consent history for one form and learner, oldest first.</summary>
    /// <remarks>
    /// The endpoint that makes the append-only design worth having: it can answer
    /// "granted on 3 September, withdrawn on 14 September, granted again on 20
    /// September" precisely. Ordered by a monotonic sequence rather than by
    /// timestamp, so two decisions made in the same second still appear in the
    /// order they were actually made.
    /// </remarks>
    [HttpGet("forms/{formId:guid}/consent/history")]
    [RequireUser]
    [ProducesResponseType(typeof(IReadOnlyList<ConsentHistoryEntry>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ConsentHistoryEntry>>> History(
        Guid formId,
        [FromQuery] [Required] Guid learnerId,
        CancellationToken cancellationToken)
    {
        var history = await consent.HistoryAsync(
            currentUser.Require(),
            formId,
            learnerId,
            cancellationToken);

        return Ok(history);
    }
}

public sealed record RecordConsentRequest
{
    [Required]
    public Guid LearnerId { get; init; }

    [Required]
    public ConsentDecision Decision { get; init; }
}

public sealed record ConsentResponse(
    Guid ConsentId,
    ConsentDecision Decision,
    string SignatureRef,
    DateTimeOffset SignedAt,
    string Message);

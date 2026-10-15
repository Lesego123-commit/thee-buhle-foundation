// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// The staff console: learners, announcements, mentorship, volunteers,
/// donations, the audit log.
/// </summary>
/// <remarks>
/// <para>
/// Staff-side by default, with two exceptions that are more restricted and are
/// marked where they appear: <strong>the audit log</strong> is the safeguarding
/// lead and administrators, and <strong>the impact refresh</strong> is
/// administrators only.
/// </para>
/// <para>
/// The safeguarding queue is not in this controller at all — it lives in
/// <see cref="SafeguardingController"/> behind a stricter gate, because ordinary
/// programme staff must not reach it.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[RequireStaff]
public sealed class AdminController(
    ILearnerRepository learners,
    IMentorshipRepository mentorships,
    IEngagementRepository engagement,
    IDonationRepository donations,
    IAuditRepository audit,
    IBuddyPairingRepository pairing,
    AnnouncementService announcements,
    MentorshipService mentorship,
    ImpactService impact,
    CurrentUserAccessor currentUser) : ControllerBase
{
    // ------------------------------------------------------------- learners

    [HttpGet("learners")]
    [ProducesResponseType(typeof(PagedResult<LearnerCard>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LearnerCard>>> Learners(
        [FromQuery] [MaxLength(80)] string? query,
        [FromQuery] [Range(1, 12)] int? grade,
        [FromQuery] [Range(1, 1000)] int page = 1,
        [FromQuery] [Range(1, 100)] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await learners.SearchAsync(query, grade, page, pageSize, cancellationToken));

    // -------------------------------------------------------- announcements

    /// <summary>Publishes an announcement.</summary>
    /// <remarks>
    /// <para>
    /// The body is <strong>sanitised on write</strong> against an allow-list of
    /// eleven tags. Sanitising on write rather than on read is the stronger
    /// choice: the database cannot contain a payload at all, so a future template
    /// that forgets to escape, or a second client that renders the field
    /// differently, is not a new vulnerability.
    /// </para>
    /// <para>
    /// Publishing writes the delivery rows for the audience and queues one
    /// notification per recipient through whichever channel each has chosen. The
    /// delivery rows are the compliance record and are not advisory; the
    /// notifications are.
    /// </para>
    /// </remarks>
    [HttpPost("announcements")]
    [ProducesResponseType(typeof(PublishResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PublishResult>> Publish(
        [FromBody] PublishAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await announcements.PublishAsync(
            currentUser.Require(),
            request.Title.Trim(),
            request.BodyHtml,
            request.Category,
            request.Audience,
            request.ActionRequired,
            request.DueDate,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Creates a permission form and the announcement that tells parents about it.</summary>
    /// <remarks>
    /// Both in one transaction, because the two go together: a permission form
    /// nobody was told about is not a permission form, and an announcement about
    /// a form that does not exist sends parents to a dead link.
    /// </remarks>
    [HttpPost("permission-forms")]
    [ProducesResponseType(typeof(PermissionFormResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PermissionFormResult>> PublishForm(
        [FromBody] PublishFormRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await announcements.PublishPermissionFormAsync(
            currentUser.Require(),
            request.Title.Trim(),
            request.Body.Trim(),
            request.DueDate,
            request.EventId,
            request.ProgrammeId,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    // ----------------------------------------------------------- mentorship

    /// <summary>Assigns a mentor to a learner for the current school year.</summary>
    /// <remarks>
    /// <para>
    /// Refused unless the mentor's police clearance is <em>current</em>. South
    /// African law requires anyone working with children to be screened against
    /// the National Child Protection Register, and a database CHECK constraint
    /// can insist that a cleared mentor has an expiry date but cannot compare it
    /// to today — so the expiry half of that rule is enforced in the service, and
    /// this is the only way to create an assignment.
    /// </para>
    /// <para>
    /// One active mentor per learner per year, enforced by a filtered unique
    /// index as well as by the service. Handing a child between mentors mid-year
    /// requires ending the existing assignment first, because that is a decision
    /// somebody has to make explicitly.
    /// </para>
    /// </remarks>
    [HttpPost("mentorships")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(
        [FromBody] AssignMentorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = await mentorship.AssignAsync(
            currentUser.Require(),
            request.MentorId,
            request.LearnerId,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new { assignmentId = id });
    }

    // ------------------------------------------------------- buddy pairings

    /// <summary>Buddy pairings awaiting approval.</summary>
    /// <remarks>
    /// Both children appear by reference code only. A staff member approving a
    /// pairing needs to know that two Grade 9 learners want to study together;
    /// they do not need a list of named children on screen to decide it.
    /// </remarks>
    [HttpGet("pairings/pending")]
    [ProducesResponseType(typeof(IReadOnlyList<PendingPairing>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PendingPairing>>> PendingPairings(
        CancellationToken cancellationToken) =>
        Ok(await pairing.PendingApprovalsAsync(cancellationToken));

    /// <summary>Approves or declines a buddy pairing.</summary>
    /// <remarks>
    /// Conditional on the request still being pending, so two staff members
    /// working the queue at once cannot have the second silently overwrite the
    /// first's decision — which in a two-person team is the normal case.
    /// </remarks>
    [HttpPost("pairings/{requestId:guid}/decide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DecidePairing(
        Guid requestId,
        [FromBody] DecidePairingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var decided = await pairing.DecideRequestAsync(
            requestId,
            request.Approve ? BuddyRequestStatus.Approved : BuddyRequestStatus.Declined,
            currentUser.Require().UserId,
            cancellationToken);

        return decided
            ? NoContent()
            : Conflict(new { detail = "Somebody has already decided that request." });
    }

    // ----------------------------------------------------------- volunteers

    /// <summary>The volunteer vetting queue.</summary>
    /// <remarks>
    /// Oldest first. Somebody who applied five weeks ago and has heard nothing is
    /// the one the queue should surface — a newest-first volunteer queue is how
    /// applicants quietly give up.
    /// </remarks>
    [HttpGet("volunteers")]
    [ProducesResponseType(typeof(PagedResult<VolunteerView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<VolunteerView>>> Volunteers(
        [FromQuery] VolunteerStatus? status,
        [FromQuery] [Range(1, 1000)] int page = 1,
        [FromQuery] [Range(1, 100)] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await engagement.VolunteerApplicationsAsync(status, page, pageSize, cancellationToken));

    [HttpPost("volunteers/{applicationId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetVolunteerStatus(
        Guid applicationId,
        [FromBody] SetStatusRequest<VolunteerStatus> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var changed = await engagement.SetVolunteerStatusAsync(
            applicationId,
            request.Status,
            currentUser.Require().UserId,
            cancellationToken);

        return changed
            ? NoContent()
            : Conflict(new { detail = "That application is already in that state." });
    }

    // ------------------------------------------------------------- contacts

    /// <summary>Contact-form messages.</summary>
    /// <remarks>
    /// Child-safety enquiries first, then oldest — regardless of arrival time.
    /// The routing has already emailed the safeguarding lead; this makes the
    /// console agree with that priority.
    /// </remarks>
    [HttpGet("contact-messages")]
    [ProducesResponseType(typeof(PagedResult<ContactView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ContactView>>> ContactMessages(
        [FromQuery] ContactStatus? status,
        [FromQuery] [Range(1, 1000)] int page = 1,
        [FromQuery] [Range(1, 100)] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await engagement.ContactMessagesAsync(status, page, pageSize, cancellationToken));

    [HttpPost("contact-messages/{messageId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetContactStatus(
        Guid messageId,
        [FromBody] SetStatusRequest<ContactStatus> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var changed = await engagement.SetContactStatusAsync(
            messageId,
            request.Status,
            currentUser.Require().UserId,
            cancellationToken);

        return changed
            ? NoContent()
            : Conflict(new { detail = "That message is already in that state." });
    }

    // ------------------------------------------------------------ donations

    /// <summary>Recent donations.</summary>
    /// <remarks>
    /// Includes pending and failed ones, which the public totals exclude. A staff
    /// member reconciling a bank statement needs to see the abandoned checkouts;
    /// the published figures must not.
    /// </remarks>
    [HttpGet("donations")]
    [ProducesResponseType(typeof(PagedResult<DonationView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DonationView>>> Donations(
        [FromQuery] DonationStatus? status,
        [FromQuery] [Range(1, 1000)] int page = 1,
        [FromQuery] [Range(1, 100)] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await donations.RecentAsync(status, page, pageSize, cancellationToken));

    [HttpGet("donations/totals")]
    [ProducesResponseType(typeof(DonationTotals), StatusCodes.Status200OK)]
    public async Task<ActionResult<DonationTotals>> Totals(CancellationToken cancellationToken) =>
        Ok(await donations.TotalsAsync(cancellationToken));

    // --------------------------------------------------------------- impact

    /// <summary>Recomputes and republishes the impact figures.</summary>
    /// <remarks>
    /// <para>
    /// Also runs nightly. Exposed as an endpoint so a staff member can refresh
    /// the page immediately after capturing a Saturday's registers, rather than
    /// showing yesterday's numbers to a funder who is in the room.
    /// </para>
    /// <para>
    /// The figures are stored rather than computed on page load, which is the
    /// whole reason this endpoint exists: a page that aggregates live can show a
    /// half-captured register as a drop in attendance.
    /// </para>
    /// </remarks>
    [HttpPost("impact/refresh")]
    [RequireAdmin]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshImpact(
        [FromQuery] int? schoolYear,
        CancellationToken cancellationToken) =>
        Ok(await impact.RefreshAsync(schoolYear, cancellationToken));

    // ------------------------------------------------------------ audit log

    /// <summary>The audit trail.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Administrators and the designated safeguarding lead.</strong> Not
    /// ordinary staff — the trail records what every staff member did, so read
    /// access to it is read access to your colleagues' activity.
    /// </para>
    /// <para>
    /// The lead is included because a safeguarding investigation may need to
    /// answer "who opened this child's record, and when?", and they are the
    /// designated child-protection officer. This matches
    /// <c>AccessPolicy.CanReadAuditLog</c>, which is the point: the route and
    /// the policy said different things until a live request showed the lead
    /// getting a 403 that the policy permitted. A policy method nothing consults
    /// is worse than no policy method.
    /// </para>
    /// <para>
    /// The projection deliberately omits the before-and-after snapshots. Those
    /// contain whatever the audited operation touched, which for a learner update
    /// includes a child's name and school — and a paged list would render dozens
    /// of children's details on one screen. They are readable per entity, where
    /// somebody has already established they may see that record.
    /// </para>
    /// </remarks>
    [HttpGet("audit")]
    [RequireSafeguarding]
    [ProducesResponseType(typeof(PagedResult<AuditView>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AuditView>>> Audit(
        [FromQuery] [Range(1, 10000)] int page = 1,
        [FromQuery] [Range(1, 100)] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await audit.RecentAsync(page, pageSize, cancellationToken));

    /// <summary>The audit trail for one entity, including the change snapshots.</summary>
    [HttpGet("audit/{entityName}/{entityId}")]
    [RequireSafeguarding]
    [ProducesResponseType(typeof(IReadOnlyList<AuditView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuditView>>> AuditForEntity(
        string entityName,
        string entityId,
        [FromQuery] [Range(1, 200)] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await audit.ForEntityAsync(entityName, entityId, limit, cancellationToken));

    /// <summary>Check-ins for one learner, for a staff member reviewing a case.</summary>
    /// <remarks>
    /// The flagged ones are included <em>only</em> for the safeguarding lead and
    /// administrators, and the filter is applied in the database query — so an
    /// ordinary staff member's request never loads a flagged note at all.
    /// </remarks>
    [HttpGet("learners/{learnerId:guid}/check-ins")]
    [ProducesResponseType(typeof(IReadOnlyList<CheckInView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CheckInView>>> CheckIns(
        Guid learnerId,
        [FromQuery] [Range(1, 50)] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var includeFlagged = Application.Security.AccessPolicy.CanReadFlaggedConcerns(currentUser.Require());

        return Ok(await mentorships.CheckInsForLearnerAsync(
            learnerId,
            limit,
            includeFlagged,
            cancellationToken));
    }
}

public sealed record PublishAnnouncementRequest
{
    [Required]
    [MaxLength(200)]
    public required string Title { get; init; }

    /// <summary>
    /// The body, as markup.
    /// </summary>
    /// <remarks>
    /// Sanitised server-side against an allow-list of eleven tags. Anything else
    /// — a script tag, an event handler, an iframe, an image — is removed before
    /// the value is stored, so what comes back out is not what was sent in.
    /// </remarks>
    [Required]
    [MaxLength(20000)]
    public required string BodyHtml { get; init; }

    [Required]
    public AnnouncementCategory Category { get; init; }

    [Required]
    public Audience Audience { get; init; }

    /// <summary>
    /// Whether parents have to do something about this.
    /// </summary>
    /// <remarks>
    /// Requires a due date, so parents know by when — and overrides the category
    /// mapping when notifications are sent, because a slip due on Friday is a
    /// deadline whatever it is nominally about.
    /// </remarks>
    public bool ActionRequired { get; init; }

    public DateOnly? DueDate { get; init; }
}

public sealed record PublishFormRequest
{
    [Required]
    [MaxLength(200)]
    public required string Title { get; init; }

    /// <summary>
    /// Plain text, not markup.
    /// </summary>
    /// <remarks>
    /// HTML-encoded into a paragraph rather than sanitised, because a permission
    /// form's body is a staff member typing a sentence and never markup —
    /// encoding is the correct treatment and cannot corrupt a message that
    /// mentions a file extension.
    /// </remarks>
    [Required]
    [MaxLength(4000)]
    public required string Body { get; init; }

    [Required]
    public DateOnly DueDate { get; init; }

    /// <summary>Exactly one of these two. Both or neither is refused.</summary>
    public Guid? EventId { get; init; }

    public Guid? ProgrammeId { get; init; }
}

public sealed record AssignMentorRequest
{
    [Required]
    public Guid MentorId { get; init; }

    [Required]
    public Guid LearnerId { get; init; }
}

public sealed record DecidePairingRequest
{
    public bool Approve { get; init; }
}

/// <summary>A status change, for the queues that have one.</summary>
public sealed record SetStatusRequest<TStatus>
    where TStatus : struct, Enum
{
    [Required]
    public TStatus Status { get; init; }
}

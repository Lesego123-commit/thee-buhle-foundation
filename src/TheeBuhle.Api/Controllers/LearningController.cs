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
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Application.Security;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Security;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// The learning hub: subjects, resources, progress and badges.
/// </summary>
/// <remarks>
/// <para>
/// Progress is always scoped to the signed-in learner, resolved from their
/// account rather than taken as a parameter. There is no endpoint that accepts
/// "whose progress" as an argument, so one learner's progress cannot appear on
/// another's screen because a caller forgot a filter.
/// </para>
/// <para>
/// The badge endpoints return no rank, no position and no percentile, and there
/// is no "top learners" route. The no-leaderboard rule is asserted by the
/// absence of the data access to build one, not by a decision in a controller.
/// </para>
/// </remarks>
[ApiController]
[Route("api/learning")]
[Produces("application/json")]
public sealed class LearningController(
    ILearningRepository learning,
    IBadgeRepository badges,
    BadgeEvaluator evaluator,
    AccessPolicy policy,
    IUrlSigner signer,
    SigningOptions signing,
    IClock clock,
    CurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Subjects for a grade, with how many resources the caller has finished.</summary>
    /// <remarks>
    /// Readable without signing in — a parent deciding whether to enrol their
    /// child should be able to see what is in the hub. The completed count is
    /// zero for an anonymous caller rather than an aggregate across every
    /// learner, so a visitor sees what exists and not somebody else's progress
    /// through it.
    /// </remarks>
    [HttpGet("subjects")]
    [ProducesResponseType(typeof(IReadOnlyList<SubjectCard>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubjectCard>>> Subjects(
        [FromQuery] [Range(1, 12)] int grade,
        CancellationToken cancellationToken) =>
        Ok(await learning.SubjectsForGradeAsync(
            grade,
            currentUser.User?.LearnerId,
            cancellationToken));

    [HttpGet("subjects/{subjectId:guid}/resources")]
    [ProducesResponseType(typeof(IReadOnlyList<ResourceCard>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ResourceCard>>> Resources(
        Guid subjectId,
        [FromQuery] [Range(1, 12)] int grade,
        [FromQuery] ResourceType? type,
        CancellationToken cancellationToken) =>
        Ok(await learning.ResourcesForSubjectAsync(
            subjectId,
            grade,
            currentUser.User?.LearnerId,
            type,
            cancellationToken));

    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<ResourceCard>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ResourceCard>>> Search(
        [FromQuery] [Required] [MaxLength(80)] string query,
        [FromQuery] [Range(1, 12)] int? grade,
        [FromQuery] [Range(1, 50)] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await learning.SearchResourcesAsync(
            query,
            grade,
            currentUser.User?.LearnerId,
            limit,
            cancellationToken));

    /// <summary>The one part-finished resource to offer as "continue where you left off".</summary>
    /// <remarks>
    /// Strictly between 1 and 99 per cent complete. Offering somebody a video
    /// they have already finished is the sort of small wrongness that makes an
    /// app feel broken.
    /// </remarks>
    [HttpGet("continue")]
    [RequireUser(Role.Learner)]
    [ProducesResponseType(typeof(ResourceCard), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<ResourceCard>> Continue(CancellationToken cancellationToken)
    {
        var learnerId = currentUser.Require().LearnerId;

        if (learnerId is null)
        {
            return NoContent();
        }

        var resource = await learning.ContinueWatchingAsync(learnerId.Value, cancellationToken);

        return resource is null ? NoContent() : Ok(resource);
    }

    /// <summary>Records how far through a resource the learner has got.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Monotonic: progress never decreases.</strong> A learner who scrubs
    /// back to the start of a video they have finished has not un-finished it.
    /// </para>
    /// <para>
    /// Without that rule, closing a completed video at the beginning would drop
    /// its percentage below 100, the completed-resource count would fall, and the
    /// badge that count earned would silently disappear from a child's screen for
    /// no reason they could see.
    /// </para>
    /// </remarks>
    [HttpPost("resources/{resourceId:guid}/progress")]
    [RequireUser(Role.Learner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RecordProgress(
        Guid resourceId,
        [FromBody] ProgressRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var learnerId = currentUser.Require().LearnerId;

        if (learnerId is null)
        {
            return NoContent();
        }

        await learning.RecordProgressAsync(
            learnerId.Value,
            resourceId,
            request.PercentComplete,
            cancellationToken);

        return NoContent();
    }

    /// <summary>Issues a short-lived signed download link for a resource.</summary>
    /// <remarks>
    /// <para>
    /// Resources are served from storage rather than proxied through the API, so
    /// the URL has to carry its own authorisation. The signature covers the
    /// resource, the requesting user <em>and</em> the expiry — so a learner who
    /// forwards their link has not handed over a working one, and editing the
    /// expiry in the URL invalidates the signature rather than extending it.
    /// </para>
    /// <para>
    /// Fifteen minutes. Long enough to start a download on a slow connection,
    /// short enough that a link pasted into a group chat stops working before
    /// anybody uses it.
    /// </para>
    /// </remarks>
    [HttpPost("resources/{resourceId:guid}/download")]
    [RequireUser]
    [ProducesResponseType(typeof(DownloadLink), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DownloadLink>> Download(
        Guid resourceId,
        CancellationToken cancellationToken)
    {
        var resource = await learning.FindResourceAsync(resourceId, cancellationToken);

        if (resource is null || resource.PublishedAt is null)
        {
            // The same answer for "does not exist" and "not published", so an
            // unpublished resource cannot be detected by guessing its id.
            return NotFound(new { detail = "That resource does not exist." });
        }

        var user = currentUser.Require();
        var expiresAt = clock.Now.Add(signing.DownloadLifetime);
        var signature = signer.SignDownload(resourceId, user.UserId, expiresAt);

        return Ok(new DownloadLink(
            resource.StoragePath,
            expiresAt,
            signature,
            resource.Title));
    }

    /// <summary>The badge catalogue for one learner, earned and unearned.</summary>
    /// <remarks>
    /// The unearned ones are included on purpose: the app shows a learner what is
    /// still available to earn, which is the part that motivates. A list of only
    /// what they already have is a trophy cabinet, not a goal.
    /// </remarks>
    [HttpGet("learners/{learnerId:guid}/badges")]
    [RequireUser]
    [ProducesResponseType(typeof(IReadOnlyList<BadgeCatalogueEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<BadgeCatalogueEntry>>> Badges(
        Guid learnerId,
        CancellationToken cancellationToken)
    {
        if (!await policy.CanReadLearnerAsync(currentUser.Require(), learnerId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { detail = "You may not view that learner." });
        }

        return Ok(await badges.CatalogueForLearnerAsync(learnerId, cancellationToken));
    }

    /// <summary>What the learner is working towards next.</summary>
    /// <remarks>
    /// <para>
    /// A tier and a next-badge hint, and <strong>no rank</strong>. There is no
    /// position, no percentile and no comparison to other learners anywhere in
    /// this response — gamification here is about a child's own progress over
    /// time, not about beating their classmates.
    /// </para>
    /// <para>
    /// That is a deliberate design constraint from Task 1 rather than a feature
    /// nobody got round to: a leaderboard in a programme for vulnerable children
    /// publicly ranks the ones who are struggling.
    /// </para>
    /// </remarks>
    [HttpGet("learners/{learnerId:guid}/milestone")]
    [RequireUser]
    [ProducesResponseType(typeof(MilestoneView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MilestoneView>> Milestone(
        Guid learnerId,
        CancellationToken cancellationToken)
    {
        if (!await policy.CanReadLearnerAsync(currentUser.Require(), learnerId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { detail = "You may not view that learner." });
        }

        return Ok(await evaluator.MilestoneAsync(learnerId, cancellationToken));
    }

    /// <summary>One learner's mark trend by subject.</summary>
    /// <remarks>
    /// <strong>First and latest mark by <em>term</em>, not by value</strong>, so
    /// the improvement figure is signed and honest. Taking the minimum and
    /// maximum instead can only ever produce a non-negative improvement — for
    /// every learner, including one whose marks collapsed — which would tell a
    /// struggling child they are improving.
    /// </remarks>
    [HttpGet("learners/{learnerId:guid}/marks")]
    [RequireUser]
    [ProducesResponseType(typeof(IReadOnlyList<SubjectMarkTrend>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<SubjectMarkTrend>>> Marks(
        Guid learnerId,
        [FromQuery] int? schoolYear,
        CancellationToken cancellationToken)
    {
        if (!await policy.CanReadLearnerAsync(currentUser.Require(), learnerId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { detail = "You may not view that learner." });
        }

        return Ok(await learning.MarkTrendAsync(
            learnerId,
            schoolYear ?? clock.Today.Year,
            cancellationToken));
    }
}

public sealed record ProgressRequest
{
    [Range(0, 100)]
    public int PercentComplete { get; init; }
}

public sealed record DownloadLink(
    string StoragePath,
    DateTimeOffset ExpiresAt,
    string Signature,
    string Title);

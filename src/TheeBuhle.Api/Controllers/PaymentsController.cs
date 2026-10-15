// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using TheeBuhle.Application.Services;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// The payment gateway's server-to-server callback.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the most security-sensitive endpoint in the system.</strong>
/// It is a public URL, it cannot require authentication because the caller is a
/// third party, and what it does is turn a pending donation into money the
/// foundation says it received — which then appears on the public impact page
/// and produces a Section 18A tax certificate a donor puts on their SARS return.
/// </para>
/// <para>
/// So it carries four independent checks, applied in
/// <c>DonationService.HandleCallbackAsync</c> in this order, each closing
/// something specific:
/// </para>
/// <list type="number">
/// <item>
/// <strong>Signature.</strong> The payload is re-signed with the shared
/// passphrase and compared in fixed time. Without it, anybody who can reach this
/// URL posts <c>payment_status=COMPLETE</c> and mints a donation.
/// </item>
/// <item>
/// <strong>Source address.</strong> The caller must resolve to a known gateway
/// host. A correct signature from an unexpected address means the passphrase has
/// leaked, which is worth refusing and logging loudly rather than accepting on
/// the strength of the signature alone.
/// </item>
/// <item>
/// <strong>Amount.</strong> The amount reported must match the amount recorded.
/// This is what stops a donor editing the hidden amount field in their browser
/// and being receipted for R5 000 after paying R10.
/// </item>
/// <item>
/// <strong>Idempotency.</strong> Completing is conditional on the donation still
/// being pending, so a callback delivered three times — which gateways do,
/// routinely — completes it once.
/// </item>
/// </list>
/// <para>
/// Note the response codes: <strong>almost everything answers 200</strong>. A
/// gateway retries anything that is not a clean 200, forever, so answering 400
/// to a payload we have decided to ignore produces an infinite retry loop. The
/// decision is recorded in the log and the body; the status tells the gateway we
/// received it.
/// </para>
/// </remarks>
[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(DonationService donations) : ControllerBase
{
    /// <summary>Receives the gateway's payment notification.</summary>
    /// <remarks>
    /// <para>
    /// Reads the form body, because that is what the gateway posts — not JSON.
    /// The whole form is passed through unfiltered so the signature can be
    /// recomputed over exactly the fields that were sent, in the order they were
    /// sent; dropping or reordering a field produces a signature that never
    /// matches, for a reason that is very hard to find.
    /// </para>
    /// <para>
    /// The endpoint is deliberately not rate limited beyond the global ceiling. A
    /// legitimate burst of donations — the morning after a fundraising email —
    /// must not be throttled into a retry storm, and the signature check is the
    /// real protection here rather than a request count.
    /// </para>
    /// </remarks>
    /// <response code="200">
    /// Received. The body says what was decided: <c>completed</c>,
    /// <c>already_complete</c>, or <c>not_complete</c>.
    /// </response>
    /// <response code="400">
    /// Refused: a bad signature, an untrusted source, an unknown reference or an
    /// amount that does not match. The only case where a non-200 is correct,
    /// because a payload that fails these checks did not come from the gateway
    /// and there is nothing to retry.
    /// </response>
    [HttpPost("callback")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);

        var payload = form.ToDictionary(
            field => field.Key,
            field => field.Value.ToString(),
            StringComparer.Ordinal);

        var outcome = await donations.HandleCallbackAsync(payload, cancellationToken);

        if (!outcome.Handled)
        {
            // 400, and deliberately without detail. A payload that fails the
            // signature or source check did not come from the gateway, so the
            // caller is either broken or hostile — and telling a hostile caller
            // *which* check it failed is free debugging assistance.
            return BadRequest(new { received = false });
        }

        // 200 with the decision in the body. The gateway only reads the status;
        // the reason is here so a staff member investigating one donation can
        // see what happened without reading application logs.
        return Ok(new { received = true, outcome = outcome.Reason });
    }

    /// <summary>Where the gateway returns the donor's browser after a payment.</summary>
    /// <remarks>
    /// <para>
    /// <strong>This endpoint changes nothing.</strong> It is a thank-you
    /// redirect, and that is the entire point: the donor controls their own
    /// browser, so arriving here proves nothing about whether money moved.
    /// </para>
    /// <para>
    /// Only the signed server-to-server callback completes a donation. A system
    /// that completed it here instead could be told "I paid" by anybody who
    /// opened the URL.
    /// </para>
    /// </remarks>
    [HttpGet("return")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Return() => Redirect("/donate.html?status=thank-you");

    /// <summary>Where the gateway returns a donor who cancelled.</summary>
    [HttpGet("cancel")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Cancel() => Redirect("/donate.html?status=cancelled");
}

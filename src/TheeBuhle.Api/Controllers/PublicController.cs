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
using Microsoft.AspNetCore.RateLimiting;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Listeners;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Api.Controllers;

/// <summary>
/// The public website's endpoints. No authentication.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is readable by anybody, which makes the <em>projections</em>
/// the security boundary rather than a permission check. The sponsor-a-child
/// cards are the clearest case: they carry a reference code, a grade, a suburb
/// and an ambition line, and no name, school or photograph — so the page cannot
/// identify a child even though anybody can read it.
/// </para>
/// <para>
/// The write endpoints are rate limited to ten an hour, which is generous for a
/// person and restrictive for a script.
/// </para>
/// </remarks>
[ApiController]
[Route("api/public")]
[Produces("application/json")]
public sealed class PublicController(
    IContentRepository content,
    ILearnerRepository learners,
    ImpactService impact,
    EventRegistrationService engagement,
    DonationService donations) : ControllerBase
{
    [HttpGet("programmes")]
    [ProducesResponseType(typeof(IReadOnlyList<ProgrammeView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProgrammeView>>> Programmes(
        CancellationToken cancellationToken) =>
        Ok(await content.ProgrammesAsync(cancellationToken));

    [HttpGet("programmes/{slug}")]
    [ProducesResponseType(typeof(ProgrammeView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProgrammeView>> Programme(
        string slug,
        CancellationToken cancellationToken)
    {
        var programme = await content.ProgrammeAsync(slug, cancellationToken);

        return programme is null
            ? NotFound(new { detail = "That programme does not exist." })
            : Ok(programme);
    }

    /// <summary>Upcoming published events, with places left.</summary>
    /// <remarks>
    /// The places-left figure is computed from the registrations, summing party
    /// sizes rather than counting bookings — so "58 places left" can be trusted
    /// rather than being a number somebody typed in and forgot.
    /// </remarks>
    [HttpGet("events")]
    [ProducesResponseType(typeof(IReadOnlyList<EventView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EventView>>> Events(
        [FromQuery] EventType? type,
        [FromQuery] [Range(1, 50)] int limit = 12,
        CancellationToken cancellationToken = default) =>
        Ok(await content.UpcomingEventsAsync(limit, type, cancellationToken));

    [HttpGet("events/{idOrSlug}")]
    [ProducesResponseType(typeof(EventView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventView>> Event(
        string idOrSlug,
        CancellationToken cancellationToken)
    {
        var publicEvent = await content.EventAsync(idOrSlug, cancellationToken);

        return publicEvent is null
            ? NotFound(new { detail = "That event does not exist, or registration is not open." })
            : Ok(publicEvent);
    }

    [HttpGet("news")]
    [ProducesResponseType(typeof(PagedResult<NewsSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NewsSummary>>> News(
        [FromQuery] NewsCategory? category,
        [FromQuery] [MaxLength(80)] string? search,
        [FromQuery] [Range(1, 1000)] int page = 1,
        [FromQuery] [Range(1, 50)] int pageSize = 9,
        CancellationToken cancellationToken = default) =>
        Ok(await content.NewsAsync(category, search, page, pageSize, cancellationToken));

    [HttpGet("news/{slug}")]
    [ProducesResponseType(typeof(NewsArticle), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NewsArticle>> Post(
        string slug,
        CancellationToken cancellationToken)
    {
        var post = await content.PostAsync(slug, cancellationToken);

        return post is null
            ? NotFound(new { detail = "That article does not exist." })
            : Ok(post);
    }

    /// <summary>The published impact figures.</summary>
    /// <remarks>
    /// Read from stored metrics rather than aggregated live, and each figure
    /// carries the time it was computed. So a visitor can see when the number was
    /// calculated, and a half-captured Saturday register cannot briefly appear as
    /// a drop in attendance on the page the foundation shows to funders.
    /// </remarks>
    [HttpGet("impact")]
    [ProducesResponseType(typeof(ImpactSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImpactSnapshot>> Impact(
        [FromQuery] int? schoolYear,
        CancellationToken cancellationToken) =>
        Ok(await impact.PublicSnapshotAsync(schoolYear, cancellationToken));

    /// <summary>The Sponsor-a-Child cards.</summary>
    /// <remarks>
    /// <para>
    /// <strong>The projection is the privacy decision.</strong> A reference code,
    /// a grade, a suburb and an ambition line. No name, no school, no
    /// photograph, no date of birth.
    /// </para>
    /// <para>
    /// Learners with an active sponsorship are excluded in the database query, so
    /// the page cannot be used to work out who is already sponsored — and a change
    /// to this controller cannot widen what it shows.
    /// </para>
    /// </remarks>
    [HttpGet("sponsorships/available")]
    [ProducesResponseType(typeof(IReadOnlyList<SponsorCard>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SponsorCard>>> Sponsorable(
        [FromQuery] [Range(1, 24)] int limit = 6,
        CancellationToken cancellationToken = default) =>
        Ok(await learners.SponsorableCardsAsync(limit, cancellationToken));

    /// <summary>Registers for a public event.</summary>
    /// <remarks>
    /// Capacity is checked inside a transaction with the event row locked, so two
    /// people registering the last place cannot both succeed. The obvious
    /// implementation — check, then insert — reads the same stale count twice and
    /// overbooks, under exactly the load that matters.
    /// </remarks>
    /// <response code="201">Registered, with places left where the event is capped.</response>
    /// <response code="409">Full, not enough places for the party size, already registered, or past.</response>
    [HttpPost("events/{idOrSlug}/register")]
    [EnableRateLimiting(RateLimiting.PublicForm)]
    [ProducesResponseType(typeof(RegistrationResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegistrationResult>> RegisterForEvent(
        string idOrSlug,
        [FromBody] EventRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await engagement.RegisterAsync(
            idOrSlug,
            request.AttendeeName.Trim(),
            request.Email.Trim(),
            request.Phone,
            request.PartySize,
            learnerId: null,
            request.ConsentPopia,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Submits the contact form.</summary>
    /// <remarks>
    /// A message whose topic is <c>Concern</c> is routed to the designated
    /// safeguarding lead's mailbox and never to the general enquiries inbox, and
    /// the response tells the sender so — along with the emergency numbers,
    /// because somebody reporting a child in danger should not have to go looking
    /// for them.
    /// </remarks>
    [HttpPost("contact")]
    [EnableRateLimiting(RateLimiting.PublicForm)]
    [ProducesResponseType(typeof(ContactResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContactResult>> Contact(
        [FromBody] ContactRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await engagement.SubmitContactMessageAsync(
            request.Topic,
            request.Name.Trim(),
            request.Organisation,
            request.Email.Trim(),
            request.Phone,
            request.Body.Trim(),
            request.ConsentPopia,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Submits a volunteer application.</summary>
    /// <remarks>
    /// The only public form that collects an ID number, because a police
    /// clearance check needs one — and only the last four digits are stored. The
    /// truncation happens server-side, so a client that posts a full number still
    /// cannot store one.
    /// </remarks>
    [HttpPost("volunteer")]
    [EnableRateLimiting(RateLimiting.PublicForm)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Volunteer(
        [FromBody] VolunteerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = await engagement.SubmitVolunteerApplicationAsync(
            request.FirstName.Trim(),
            request.Surname.Trim(),
            request.Email.Trim(),
            request.Phone.Trim(),
            request.Suburb,
            request.IdNumber,
            request.Skills ?? [],
            request.Availability ?? new Dictionary<string, IReadOnlyList<string>>(),
            request.Experience,
            request.ConsentPopia,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            applicationId = id,
            message = "Thank you. We will be in touch within a week to arrange your police clearance "
                + "and our safeguarding induction — we handle the SAPS application for you.",
        });
    }

    /// <summary>Subscribes to the newsletter.</summary>
    /// <remarks>
    /// Always answers the same way, whether the address was new, already
    /// subscribed, or previously unsubscribed. Differentiating would turn the
    /// form into a way of testing whether a given person supports this
    /// organisation, one address at a time.
    /// </remarks>
    [HttpPost("newsletter")]
    [EnableRateLimiting(RateLimiting.PublicForm)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Subscribe(
        [FromBody] NewsletterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await engagement.SubscribeToNewsletterAsync(request.Email.Trim(), cancellationToken);

        return Ok(new
        {
            subscribed = true,
            message = "You are on the list. One email a month, and every one has an unsubscribe link.",
        });
    }

    /// <summary>Starts a donation.</summary>
    /// <remarks>
    /// <para>
    /// Returns the signed field set the browser posts to the gateway's own
    /// domain. <strong>No card data touches this system</strong> — the donor
    /// enters their card on the gateway's page, and this application only ever
    /// sees a reference and an amount.
    /// </para>
    /// <para>
    /// The donation is created as <em>pending</em>. Only the verified
    /// server-to-server callback moves it to complete, so nothing on the public
    /// impact page and no tax certificate can be triggered by a donor who
    /// abandoned checkout.
    /// </para>
    /// </remarks>
    [HttpPost("donations")]
    [EnableRateLimiting(RateLimiting.PublicForm)]
    [ProducesResponseType(typeof(CheckoutHandoff), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CheckoutHandoff>> Donate(
        [FromBody] DonationRequest request,
        [FromServices] Section18AOptions section18A,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(section18A);

        var handoff = await donations.InitiateAsync(
            request.DisplayName.Trim(),
            request.Email.Trim(),
            request.Amount,
            request.Frequency,
            // Never recorded as wanted while the foundation cannot issue one,
            // or the console would show certificates "pending" that never come.
            request.WantsSection18A && section18A.CanIssueCertificates,
            request.Phone,
            request.LearnerReference,
            request.ConsentPopia,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, handoff);
    }

    /// <summary>The "what your gift buys" copy.</summary>
    /// <remarks>
    /// Served from the API rather than hard-coded in the front end so the
    /// website, the donation email and the mobile app describe a gift in the same
    /// words.
    /// </remarks>
    [HttpGet("donations/impact-copy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> ImpactCopy() => Ok(new
    {
        suggested = DonationService.SuggestedAmounts,
        copy = DonationService.ImpactCopy,
    });
}

// -----------------------------------------------------------------------------
//  The public request shapes.
//
//  Every message below is written for the person filling in the form, because
//  this is what they read. Left to itself the framework answers a short body
//  with "The field Body must be a string or array type with a minimum length of
//  '10'", which tells a parent nothing they can act on and reads like a fault
//  in the website rather than something they can fix.
//
//  Note also what a [Required] on a bool does NOT do. A non-nullable bool
//  always has a value, so Required is satisfied by `false` — an unticked
//  consent box passes validation. Consent is therefore enforced in the service,
//  where refusing is a domain rule rather than a model-binding accident, and
//  where a second client cannot get around it.
// -----------------------------------------------------------------------------

public sealed record EventRegistrationRequest
{
    [Required(ErrorMessage = "Please give us a name for the booking.")]
    [MaxLength(140, ErrorMessage = "That name is longer than we can store. Please shorten it.")]
    public required string AttendeeName { get; init; }

    [Required(ErrorMessage = "We need an email address to send your confirmation to.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address. Please check it.")]
    [MaxLength(190, ErrorMessage = "That email address is longer than we can store.")]
    public required string Email { get; init; }

    [MaxLength(30, ErrorMessage = "That phone number is longer than we can store.")]
    public string? Phone { get; init; }

    /// <summary>A family, not a coach party.</summary>
    [Range(1, 20, ErrorMessage = "A booking can be for between 1 and 20 people. "
        + "For a larger group, please phone us so we can plan the space.")]
    public int PartySize { get; init; } = 1;

    /// <summary>
    /// Consent to hold these details in order to register the attendee.
    /// </summary>
    /// <remarks>
    /// Enforced in the service, not by this attribute — see the note above.
    /// </remarks>
    public bool ConsentPopia { get; init; }
}

public sealed record ContactRequest
{
    [Required(ErrorMessage = "Please choose what your message is about.")]
    public ContactTopic Topic { get; init; }

    [Required(ErrorMessage = "Please tell us your name.")]
    [MaxLength(140, ErrorMessage = "That name is longer than we can store. Please shorten it.")]
    public required string Name { get; init; }

    [MaxLength(140, ErrorMessage = "That organisation name is longer than we can store.")]
    public string? Organisation { get; init; }

    [Required(ErrorMessage = "We need an email address so that we can reply to you.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address. Please check it.")]
    [MaxLength(190, ErrorMessage = "That email address is longer than we can store.")]
    public required string Email { get; init; }

    [MaxLength(30, ErrorMessage = "That phone number is longer than we can store.")]
    public string? Phone { get; init; }

    [Required(ErrorMessage = "Please tell us what you need.")]
    [MinLength(10, ErrorMessage = "Please tell us a little more — at least a sentence.")]
    [MaxLength(4000, ErrorMessage = "That message is very long. Please shorten it, or phone us instead.")]
    public required string Body { get; init; }

    /// <summary>
    /// Consent to hold these details.
    /// </summary>
    /// <remarks>
    /// Required by POPIA before the message can be stored at all. Checked in the
    /// service rather than only here, so a client that omits it cannot store an
    /// enquiry without consent.
    /// </remarks>
    public bool ConsentPopia { get; init; }
}

public sealed record VolunteerRequest
{
    [Required(ErrorMessage = "Please tell us your first name.")]
    [MaxLength(80, ErrorMessage = "That name is longer than we can store. Please shorten it.")]
    public required string FirstName { get; init; }

    [Required(ErrorMessage = "Please tell us your surname.")]
    [MaxLength(80, ErrorMessage = "That surname is longer than we can store. Please shorten it.")]
    public required string Surname { get; init; }

    [Required(ErrorMessage = "We need an email address to reply to your application.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address. Please check it.")]
    [MaxLength(190, ErrorMessage = "That email address is longer than we can store.")]
    public required string Email { get; init; }

    [Required(ErrorMessage = "We need a mobile number — we phone every applicant before their induction.")]
    [MaxLength(30, ErrorMessage = "That phone number is longer than we can store.")]
    public required string Phone { get; init; }

    [MaxLength(80, ErrorMessage = "That suburb name is longer than we can store.")]
    public string? Suburb { get; init; }

    /// <summary>
    /// A South African ID number, for the police clearance check.
    /// </summary>
    /// <remarks>
    /// <strong>Only the last four digits are stored.</strong> The rest is
    /// discarded server-side before anything is persisted — POPIA data
    /// minimisation applied at the point of collection, because four digits are
    /// enough for a staff member to confirm they hold the right certificate and a
    /// full ID number in the database is a liability with no use.
    /// </remarks>
    [MaxLength(20, ErrorMessage = "A South African ID number is 13 digits. Please check it.")]
    public string? IdNumber { get; init; }

    public IReadOnlyList<string>? Skills { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Availability { get; init; }

    [MaxLength(4000, ErrorMessage = "That is very long. Please summarise it — we will ask for the rest at your induction.")]
    public string? Experience { get; init; }

    /// <summary>Consent to POPIA processing and to the child-safeguarding code of conduct.</summary>
    /// <remarks>Enforced in the service, not by an attribute — see the note above.</remarks>
    public bool ConsentPopia { get; init; }
}

public sealed record NewsletterRequest
{
    [Required(ErrorMessage = "Please give us an email address.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address. Please check it.")]
    [MaxLength(190, ErrorMessage = "That email address is longer than we can store.")]
    public required string Email { get; init; }
}

public sealed record DonationRequest
{
    [Required(ErrorMessage = "Please give us a name for the receipt.")]
    [MaxLength(140, ErrorMessage = "That name is longer than we can store. Please shorten it.")]
    public required string DisplayName { get; init; }

    [Required(ErrorMessage = "We need an email address to send your receipt to.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address. Please check it.")]
    [MaxLength(190, ErrorMessage = "That email address is longer than we can store.")]
    public required string Email { get; init; }

    [Range(10, 1_000_000, ErrorMessage = "Please give an amount between R10 and R1 000 000. "
        + "For a larger gift, contact us directly so we can arrange it properly.")]
    public decimal Amount { get; init; }

    [Required(ErrorMessage = "Please choose whether this is a once-off gift, a monthly one, or a sponsorship.")]
    public DonationFrequency Frequency { get; init; }

    [MaxLength(30, ErrorMessage = "That phone number is longer than we can store.")]
    public string? Phone { get; init; }

    /// <summary>
    /// Whether a Section 18A tax certificate is wanted. Defaults to true.
    /// </summary>
    /// <remarks>
    /// Defaulted on rather than off, because a South African donor to a
    /// registered PBO is entitled to the deduction and most want it — and a
    /// certificate that was not requested is easier to ignore than one that was
    /// never issued.
    /// </remarks>
    public bool WantsSection18A { get; init; } = true;

    /// <summary>The learner to sponsor, by reference code. Required when sponsoring.</summary>
    [MaxLength(12, ErrorMessage = "That does not look like a learner reference code.")]
    public string? LearnerReference { get; init; }

    /// <summary>
    /// Consent to hold the donor's details for the receipt and the certificate.
    /// </summary>
    /// <remarks>Enforced in the service, not by an attribute — see the note above.</remarks>
    public bool ConsentPopia { get; init; }
}

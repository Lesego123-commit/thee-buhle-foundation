// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
// =============================================================================

using TheeBuhle.Domain.Common;

namespace TheeBuhle.Domain.Entities;

// =============================================================================
// ERD view 4 of 4 — engagement, communication and consent
// Task 1 documentation, Figure 2d.
// =============================================================================

/// <summary>
/// Somebody who gives money. Most donors never create an account, which is why
/// <see cref="UserId"/> is nullable.
/// </summary>
public class Donor : Entity
{
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>
    /// Unique, so a monthly giver does not accumulate a new donor row every
    /// month — which would break the Section 18A annual total.
    /// </summary>
    public required string Email { get; set; }

    public string? Phone { get; set; }

    public bool IsOrganisation { get; set; }

    public string? TaxNumber { get; set; }

    public ICollection<Donation> Donations { get; set; } = [];
    public ICollection<Sponsorship> Sponsorships { get; set; } = [];
}

/// <summary>
/// One gift.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No card data is stored anywhere in this entity, and there is no
/// property for it.</strong> The donor's card is entered on the payment
/// provider's own site; all that reaches this system is an amount, a frequency
/// and <see cref="GatewayReference"/>.
/// </para>
/// <para>
/// That reference is unique, which is what makes duplicate webhook delivery
/// idempotent — gateways redeliver, and a unique index turns a redelivery into a
/// no-op rather than a double count.
/// </para>
/// </remarks>
public class Donation : Entity
{
    public Guid DonorId { get; set; }

    public Donor Donor { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "ZAR";

    public DonationFrequency Frequency { get; set; }

    /// <summary>
    /// What the gift is for. Carries <c>sponsorship:{learnerId}</c> when the
    /// donor chose to sponsor a specific learner, so the sponsorship can be
    /// created once the payment actually completes.
    /// </summary>
    public string? Designation { get; set; }

    public DonationStatus Status { get; set; } = DonationStatus.Pending;

    public string Gateway { get; set; } = "payfast";

    public required string GatewayReference { get; set; }

    public bool WantsSection18A { get; set; } = true;

    /// <summary>
    /// The donor's consent to hold their details, as given on the form.
    /// </summary>
    /// <remarks>
    /// Recorded rather than assumed. The donation form asks for this consent
    /// and will not submit without it, so not storing the answer would mean the
    /// organisation had asked a POPIA question and kept no record of what it
    /// was told — which is worse than not asking, because the form implies a
    /// record exists.
    /// </remarks>
    public bool ConsentPopia { get; set; }

    /// <summary>
    /// When the tax certificate was emailed. Null means it still has to go out,
    /// which is how the nightly job finds donations whose certificate failed to
    /// send rather than losing them.
    /// </summary>
    public DateTimeOffset? Section18AIssuedAt { get; set; }

    /// <summary>
    /// When the gateway confirmed the money. Null while pending — and only
    /// completed donations appear in any published total, so a donor who
    /// abandoned checkout never shows up on the website.
    /// </summary>
    public DateTimeOffset? ReceivedAt { get; set; }
}

/// <summary>
/// A donor funding one named learner for a year.
/// </summary>
/// <remarks>
/// The most access-restricted relationship in the schema: the only place an
/// external party is associated with an identified child. Readable by the
/// administrator and the safeguarding lead only — not by programme staff.
/// </remarks>
public class Sponsorship : Entity
{
    public Guid DonorId { get; set; }

    public Donor Donor { get; set; } = null!;

    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public decimal AnnualAmount { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool IsActiveOn(DateOnly date) => StartDate <= date && (EndDate is null || EndDate > date);
}

/// <summary>A public event: the 5KM run, a hike, an open day, a workshop.</summary>
public class PublicEvent : Entity
{
    public required string Title { get; set; }

    public required string Slug { get; set; }

    public EventType Type { get; set; }

    public Guid? VenueId { get; set; }

    public Venue? Venue { get; set; }

    /// <summary>Free text, for events at a place that is not one of our venues.</summary>
    public string? Location { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public decimal Fee { get; set; }

    /// <summary>"R120 entry · under-16s free".</summary>
    public string? FeeNote { get; set; }

    /// <summary>Zero means unlimited, which is why places-left is nullable.</summary>
    public int Capacity { get; set; }

    public string? Description { get; set; }

    public bool IsPublished { get; set; } = true;

    public ICollection<EventRegistration> Registrations { get; set; } = [];
}

/// <summary>
/// Somebody signed up for an event.
/// </summary>
/// <remarks>
/// <see cref="LearnerId"/> is nullable because the public may register without
/// being on a programme. A unique index on (event, email) means a
/// double-submitted form cannot double-count against a capacity the fire officer
/// cares about.
/// </remarks>
public class EventRegistration : Entity
{
    public Guid EventId { get; set; }

    public PublicEvent Event { get; set; } = null!;

    public Guid? LearnerId { get; set; }

    public Learner? Learner { get; set; }

    public required string AttendeeName { get; set; }

    public required string Email { get; set; }

    public string? Phone { get; set; }

    public int PartySize { get; set; } = 1;

    public bool Paid { get; set; }

    /// <summary>
    /// The attendee's consent to hold their details, as given on the form.
    /// </summary>
    /// <remarks>
    /// Same reasoning as <see cref="Donation.ConsentPopia"/>: the registration
    /// form asks, so the answer is kept. A registration is also the one public
    /// form a parent may fill in on a child's behalf, which makes the record of
    /// who consented to what worth more than usual.
    /// </remarks>
    public bool ConsentPopia { get; set; }
}

/// <summary>
/// An application to volunteer.
/// </summary>
/// <remarks>
/// Note <see cref="IdNumberLast4"/>. The full South African ID number is needed
/// for the SAPS clearance application, which the foundation submits on the
/// applicant's behalf — and is <em>not</em> needed afterwards. Only the last four
/// digits are retained, which is enough to match a returned certificate to an
/// application. That is POPIA data minimisation expressed as a column type, and
/// the volunteer form says so at the point of asking.
/// </remarks>
public class VolunteerApplication : Entity
{
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public required string FirstName { get; set; }

    public required string Surname { get; set; }

    public required string Email { get; set; }

    public required string Phone { get; set; }

    public string? Suburb { get; set; }

    /// <summary>The last four digits only. See the remarks on this class.</summary>
    public string? IdNumberLast4 { get; set; }

    /// <summary>
    /// What the applicant can help with. JSON: the vocabulary varies and is
    /// never queried relationally.
    /// </summary>
    public List<string> Skills { get; set; } = [];

    /// <summary>Slot to days, e.g. <c>{"Morning": ["Sat"]}</c>. Also JSON.</summary>
    public Dictionary<string, List<string>> Availability { get; set; } = [];

    public string? Experience { get; set; }

    public VolunteerStatus Status { get; set; } = VolunteerStatus.Submitted;

    public bool ConsentPopia { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public ICollection<ComplianceDocument> Documents { get; set; } = [];

    public string FullName => $"{FirstName} {Surname}";
}

/// <summary>
/// A screening document: an ID copy, a police clearance, a reference.
/// </summary>
/// <remarks>
/// Points at a restricted Blob container that no application role can
/// enumerate, reachable only through a short-lived user-delegation SAS. Rows are
/// retained for seven years and then purged by a scheduled job.
/// </remarks>
public class ComplianceDocument : Entity
{
    public Guid ApplicationId { get; set; }

    public VolunteerApplication Application { get; set; } = null!;

    public ComplianceDocumentType DocType { get; set; }

    public required string StoragePath { get; set; }

    public Guid? VerifiedBy { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public DateOnly? ExpiresAt { get; set; }
}

/// <summary>
/// A notice from staff to parents, learners or mentors.
/// </summary>
/// <remarks>
/// <see cref="BodyHtml"/> is the only field in the system stored as markup, and
/// it is sanitised against an allow-list <em>on write</em>. Sanitising on write
/// rather than on read is the stronger choice: the database cannot contain a
/// payload at all, so a future template that forgets to escape, or a second
/// client that renders the field differently, is not a new vulnerability.
/// </remarks>
public class Announcement : Entity
{
    public required string Title { get; set; }

    /// <summary>Allow-list sanitised before it is ever stored.</summary>
    public required string BodyHtml { get; set; }

    public AnnouncementCategory Category { get; set; } = AnnouncementCategory.General;

    public Audience Audience { get; set; } = Audience.All;

    /// <summary>
    /// Whether parents have to do something. An announcement with this set must
    /// carry a <see cref="DueDate"/>, so a deadline is never implied without a
    /// date.
    /// </summary>
    public bool ActionRequired { get; set; }

    public DateOnly? DueDate { get; set; }

    public Guid PublishedBy { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public ICollection<MessageRecipient> Recipients { get; set; } = [];
}

/// <summary>
/// Delivery of one announcement to one user, with a read receipt.
/// </summary>
/// <remarks>
/// For a permission-form notice this row is the foundation's evidence that the
/// parent was told, and <see cref="ReadAt"/> is evidence they opened it. That is
/// why marking a message read is a server-side state change rather than a
/// client-side flag.
/// </remarks>
public class MessageRecipient
{
    public Guid AnnouncementId { get; set; }

    public Announcement Announcement { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset DeliveredAt { get; set; }
}

/// <summary>
/// A form asking a guardian's permission for an activity.
/// </summary>
/// <remarks>
/// A CHECK constraint requires exactly one of <see cref="EventId"/> and
/// <see cref="ProgrammeId"/> to be set: a permission form attached to nothing is
/// meaningless, and one attached to both is ambiguous.
/// </remarks>
public class PermissionForm : Entity
{
    public Guid? EventId { get; set; }

    public PublicEvent? Event { get; set; }

    public Guid? ProgrammeId { get; set; }

    public Programme? Programme { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    public DateOnly DueDate { get; set; }

    public Guid CreatedBy { get; set; }

    public ICollection<Consent> Consents { get; set; } = [];
}

/// <summary>
/// A guardian's decision on a permission form. <strong>Append-only.</strong>
/// </summary>
/// <remarks>
/// <para>
/// A withdrawal is a new row with <see cref="ConsentDecision.Withdrawn"/>, never
/// an update of the granted row, and nothing is ever deleted. POPIA requires the
/// full consent history to stay reconstructible: if a parent asks what they
/// agreed to and when they changed their mind, the system must be able to answer
/// precisely.
/// </para>
/// <para>
/// The rule is enforced three times over, deliberately: the repository exposes
/// an append method and no update or delete; database triggers refuse UPDATE and
/// DELETE on the table; and the service only ever appends.
/// </para>
/// <para>
/// <see cref="Sequence"/> is the ordering, <em>not</em> <see cref="SignedAt"/>.
/// A parent who grants and then immediately withdraws produces two rows with
/// identical timestamps, and resolving "the decision that stands" by MAX(time)
/// then ties — which made the answer whatever order the storage engine happened
/// to return. For the organisation's evidence of what a guardian agreed to on
/// behalf of a child, that is not acceptable. An append-only log needs a
/// monotonic sequence, so it has one. This was found by exercising the running
/// API, not by reading the code, and there is a regression test for it.
/// </para>
/// </remarks>
public class Consent : Entity
{
    /// <summary>
    /// A monotonic sequence. The authority on the order of a consent history —
    /// see the remarks on this class for why a timestamp is not.
    /// </summary>
    public long Sequence { get; set; }

    public Guid FormId { get; set; }

    public PermissionForm Form { get; set; } = null!;

    public Guid LearnerId { get; set; }

    public Learner Learner { get; set; } = null!;

    public Guid ParentId { get; set; }

    public ParentGuardian Parent { get; set; } = null!;

    public ConsentDecision Decision { get; set; }

    public DateTimeOffset SignedAt { get; set; }

    /// <summary>
    /// An HMAC over the facts being consented to — form, learner, parent,
    /// decision, time and address — keyed with a server-side secret.
    /// </summary>
    /// <remarks>
    /// Not an image of a signature and not a typed name. What is being recorded
    /// is <em>that this guardian, authenticated, made this decision at this time
    /// from this address</em>, and a keyed hash over exactly those facts is
    /// better evidence than a picture that could have been copied from another
    /// form. Because the key is server-side only, the reference also shows later
    /// that the stored row has not been edited to say something else.
    /// </remarks>
    public required string SignatureRef { get; set; }

    public string? IpAddress { get; set; }
}

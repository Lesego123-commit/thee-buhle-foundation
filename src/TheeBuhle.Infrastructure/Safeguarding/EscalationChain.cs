// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Safeguarding;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Infrastructure.Safeguarding;

// =============================================================================
//  The safeguarding escalation chain.
//
//  mentor lead → programme manager → safeguarding lead (terminal)
//
//  Two properties matter more than anything else in this file:
//
//   1. THE CHAIN NEVER SILENTLY STOPS. The terminal handler always escalates,
//      and if it cannot, RouteAsync throws rather than returning null. A mentor
//      who is told their concern was reported, when it reached nobody, is the
//      worst failure this system can produce.
//
//   2. AN URGENT CONCERN SKIPS THE FIRST TWO LINKS. A concern about a child's
//      home, safety or wellbeing may be about an adult in that child's life —
//      possibly one of the adults the first two links represent — so it goes
//      straight to the designated lead.
//
// =============================================================================

/// <summary>
/// Link 1: the mentor lead.
/// </summary>
/// <remarks>
/// <para>
/// Handles the ordinary pastoral case: a learner who is wobbling on confidence
/// or exam technique, where the person to talk to is the mentor's own lead.
/// </para>
/// <para>
/// Declines two categories, for two different reasons:
/// </para>
/// <list type="bullet">
/// <item>
/// <strong>Urgent concerns</strong> — declined because they are too serious for
/// this link, and may be about an adult close to the child.
/// </item>
/// <item>
/// <strong>Programme concerns</strong> (attendance, transport, venue, sessions)
/// — declined not because they are too serious but because the mentor lead
/// <em>cannot fix them</em>. A learner who keeps missing sessions because the
/// taxi fare went up needs a programme decision, not a pastoral conversation.
/// </item>
/// </list>
/// <para>
/// That second category is a bug fix, not a feature. In the first version of
/// this chain the mentor lead absorbed every non-urgent concern, which made the
/// programme manager link <strong>unreachable</strong> — the chain had three
/// links and used two. It was caught by a test asserting that an attendance
/// concern reaches the programme manager, and it is the reason that test exists.
/// </para>
/// </remarks>
public sealed class MentorLeadHandler(
    IUserRepository users,
    ILogger<MentorLeadHandler> logger) : ConcernHandler
{
    protected override async Task<Guid?> AttemptAsync(
        ConcernFlagged concern,
        CancellationToken cancellationToken)
    {
        if (concern.IsUrgent)
        {
            return null;
        }

        // The shared vocabulary, so adding a programme area in one place adds
        // it everywhere rather than in two of the three that need it.
        if (concern.SupportAreas.AnyProgramme())
        {
            ChainLog.LinkDeclined(logger, nameof(MentorLeadHandler), "cannot action a programme concern");

            return null;
        }

        var lead = await users.FindMentorLeadAsync(cancellationToken);

        if (lead is null)
        {
            // Not a decline on policy grounds — there is simply nobody in the
            // role. Logged separately, because "no mentor lead is configured" is
            // an operational problem somebody should fix.
            ChainLog.LinkUnavailable(logger, nameof(MentorLeadHandler));

            return null;
        }

        return lead.UserId;
    }

    public override string Describe() =>
        "Mentor lead — ordinary pastoral concerns, excluding urgent and programme matters.";
}

/// <summary>
/// Link 2: the programme manager.
/// </summary>
/// <remarks>
/// Handles the concerns the mentor lead passed on because they are programme
/// decisions: attendance, transport, venue, session scheduling. Still declines
/// urgent concerns — a programme manager is programme staff, and Task 1 section
/// 9.3.13.7 commits to safeguarding concerns being handled by the designated
/// lead rather than by programme staff.
/// </remarks>
public sealed class ProgrammeManagerHandler(
    IUserRepository users,
    ILogger<ProgrammeManagerHandler> logger) : ConcernHandler
{
    protected override async Task<Guid?> AttemptAsync(
        ConcernFlagged concern,
        CancellationToken cancellationToken)
    {
        if (concern.IsUrgent)
        {
            return null;
        }

        var manager = await users.FindProgrammeManagerAsync(cancellationToken);

        if (manager is null)
        {
            ChainLog.LinkUnavailable(logger, nameof(ProgrammeManagerHandler));

            return null;
        }

        return manager.UserId;
    }

    public override string Describe() =>
        "Programme manager — attendance, transport, venue and session concerns.";
}

/// <summary>
/// Link 3, terminal: the designated safeguarding lead.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This handler never declines.</strong> Every urgent concern arrives
/// here by design, and every concern the first two links could not take arrives
/// here as a backstop. It is the only link with no condition on the concern at
/// all.
/// </para>
/// <para>
/// The single case in which it returns null is that the organisation has no
/// safeguarding lead on record — which is not a routing decision but a
/// configuration failure, and it becomes an
/// <see cref="UnroutableConcernException"/> in <see cref="EscalationChain"/>
/// rather than a quiet null. The database's filtered unique index guarantees
/// there is at most one lead; nothing guarantees there is at least one, so this
/// is where that gap is caught.
/// </para>
/// </remarks>
public sealed class SafeguardingLeadHandler(
    IUserRepository users,
    ILogger<SafeguardingLeadHandler> logger) : ConcernHandler
{
    protected override async Task<Guid?> AttemptAsync(
        ConcernFlagged concern,
        CancellationToken cancellationToken)
    {
        var lead = await users.FindSafeguardingLeadAsync(cancellationToken);

        if (lead is null)
        {
            // Escalated to the log at Critical, because the system currently
            // cannot receive a child-safety report and somebody has to know
            // within minutes rather than at the next review.
            ChainLog.NoSafeguardingLead(logger);

            return null;
        }

        return lead.UserId;
    }

    public override string Describe() =>
        "Designated safeguarding lead — every urgent concern, and anything the earlier links could not take.";
}

/// <summary>
/// Assembles the chain and routes through it.
/// </summary>
/// <remarks>
/// <para>
/// A new chain is built per call. The links are cheap objects and the chain is
/// stateful — <c>SetNext</c> mutates it — so a shared instance reconfigured
/// concurrently by two requests would be a real hazard for no gain.
/// </para>
/// </remarks>
public sealed class EscalationChain(
    MentorLeadHandler mentorLead,
    ProgrammeManagerHandler programmeManager,
    SafeguardingLeadHandler safeguardingLead,
    ILogger<EscalationChain> logger) : IEscalationChain
{
    public async Task<Guid> RouteAsync(
        ConcernFlagged concern,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(concern);

        // The organisation's child-protection policy, written once, in order.
        mentorLead.SetNext(programmeManager).SetNext(safeguardingLead);

        var recipient = await mentorLead.HandleAsync(concern, cancellationToken);

        if (recipient is { } userId)
        {
            ChainLog.ConcernRouted(logger, concern.LearnerReference, concern.IsUrgent);

            return userId;
        }

        // Every link declined or was unavailable. Thrown rather than returned,
        // because the caller is inside a transaction that must now roll back:
        // the check-in is not saved, the API answers 503, and the mentor is
        // asked to phone. Loud, and deliberately so — the alternative is a
        // mentor who believes a child's situation has been reported when it has
        // reached nobody at all.
        throw new UnroutableConcernException(
            "This concern could not be routed to anybody. It has NOT been saved. "
            + "Please phone the safeguarding lead directly on +27 71 000 0001.");
    }

    public IReadOnlyList<string> Describe() =>
    [
        mentorLead.Describe(),
        programmeManager.Describe(),
        safeguardingLead.Describe(),
    ];
}

internal static partial class ChainLog
{
    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Debug,
        Message = "Escalation link {Link} declined: {Reason}")]
    public static partial void LinkDeclined(ILogger logger, string link, string reason);

    [LoggerMessage(
        EventId = 3102,
        Level = LogLevel.Warning,
        Message = "Escalation link {Link} has nobody in the role — the concern was passed on")]
    public static partial void LinkUnavailable(ILogger logger, string link);

    [LoggerMessage(
        EventId = 3103,
        Level = LogLevel.Information,
        Message = "Concern for {LearnerReference} routed (urgent: {Urgent})")]
    public static partial void ConcernRouted(ILogger logger, string learnerReference, bool urgent);

    [LoggerMessage(
        EventId = 3104,
        Level = LogLevel.Critical,
        Message = "NO DESIGNATED SAFEGUARDING LEAD IS CONFIGURED. "
            + "Flagged concerns cannot be routed and check-ins that flag one will be refused.")]
    public static partial void NoSafeguardingLead(ILogger logger);
}

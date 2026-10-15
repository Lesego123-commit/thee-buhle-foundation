// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using TheeBuhle.Domain.Events;

namespace TheeBuhle.Application.Safeguarding;

/// <summary>
/// One link in the safeguarding escalation chain.
/// </summary>
/// <remarks>
/// <para>
/// Task 1 section 9.3.12.1 chose Chain of Responsibility here for a specific
/// reason: a flagged concern must reach whoever is competent and available to
/// act, and must never silently stop. Encoding the organisation's
/// child-protection policy as a chain of objects rather than a nest of
/// <c>if</c> statements means the order is visible in one place and cannot be
/// accidentally reordered by somebody editing a condition halfway down a long
/// method.
/// </para>
/// <para>The contract every handler must honour:</para>
/// <list type="bullet">
/// <item>Either it handles the concern and returns the user id it was routed to,</item>
/// <item>or it returns null and the chain continues.</item>
/// <item>The terminal handler <strong>always</strong> escalates. It may not
/// return null.</item>
/// </list>
/// </remarks>
public abstract class ConcernHandler
{
    private ConcernHandler? _next;

    /// <summary>Links the next handler and returns it, so a chain reads as a sentence.</summary>
    public ConcernHandler SetNext(ConcernHandler next)
    {
        _next = next;

        return next;
    }

    /// <returns>The user id the concern was routed to, or null to pass it on.</returns>
    public async Task<Guid?> HandleAsync(ConcernFlagged concern, CancellationToken cancellationToken)
    {
        var handledBy = await AttemptAsync(concern, cancellationToken);

        if (handledBy is not null)
        {
            return handledBy;
        }

        return _next is null ? null : await _next.HandleAsync(concern, cancellationToken);
    }

    /// <returns>A user id if this handler took it; null to pass on.</returns>
    protected abstract Task<Guid?> AttemptAsync(ConcernFlagged concern, CancellationToken cancellationToken);

    /// <summary>A one-line description, used in the DevOps documentation and the tests.</summary>
    public abstract string Describe();
}

/// <summary>
/// Assembles the chain and exposes it as one call.
/// </summary>
/// <remarks>
/// <para>The order is the organisation's policy, written once:</para>
/// <para><c>mentor lead → programme manager → safeguarding lead (terminal)</c></para>
/// <para>
/// Holding the assembly in its own type means the order is testable on its own —
/// the chain tests assert that an urgent concern skips the first two links, and
/// that the chain never returns null — and that a future change to the policy is
/// a one-line edit in a file whose only job is that policy.
/// </para>
/// </remarks>
public interface IEscalationChain
{
    /// <summary>
    /// Routes a flagged concern.
    /// </summary>
    /// <returns>The user id it was routed to.</returns>
    /// <exception cref="Domain.Common.UnroutableConcernException">
    /// If nobody can receive it. That aborts the enclosing transaction so the
    /// check-in is not saved, and the mentor is told to phone instead.
    /// </exception>
    Task<Guid> RouteAsync(ConcernFlagged concern, CancellationToken cancellationToken = default);

    /// <summary>The links, in order. Rendered in the DevOps documentation.</summary>
    IReadOnlyList<string> Describe();
}

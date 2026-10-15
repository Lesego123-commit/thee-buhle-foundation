// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

namespace TheeBuhle.Domain.Common;

/// <summary>
/// Something that has happened in the domain.
/// </summary>
/// <remarks>
/// Events are named in the past tense and are immutable records. A handler
/// receives a statement of fact, not a request — which is what lets the number
/// of handlers grow without the service that raised the event ever changing.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>A stable name for the log. Not used for dispatch.</summary>
    string Name { get; }

    /// <summary>
    /// A loggable summary that contains no personal information.
    /// </summary>
    /// <remarks>
    /// Implementations deliberately expose the learner's pseudonymous reference
    /// code rather than their name, and never the text of a safeguarding note.
    /// A log file is not a place for an identified child.
    /// </remarks>
    IReadOnlyDictionary<string, object?> Context { get; }
}

/// <summary>
/// Handles one consequence of one kind of event.
/// </summary>
/// <remarks>
/// A handler that throws aborts the enclosing transaction, which is usually
/// what is wanted: a badge is part of the truth about a learner and belongs in
/// the same commit as the attendance row that earned it. Handlers whose failure
/// must <em>not</em> undo the work additionally implement
/// <see cref="IAdvisoryListener"/>.
/// </remarks>
public interface IDomainEventListener<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Marks a handler whose failure must not fail the transaction.
/// </summary>
/// <remarks>
/// The distinction matters. "Award the badge" is part of the truth about the
/// learner. "Send the parent a push notification" depends on a third party being
/// up, and a mentor standing in a hall in Sebokeng must not see a register
/// refuse to save because a push service timed out.
/// <para>
/// Marking a handler advisory is a statement about consequence, and the
/// dispatcher enforces it.
/// </para>
/// </remarks>
public interface IAdvisoryListener;

/// <summary>
/// Publishes domain events to their handlers.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent;
}

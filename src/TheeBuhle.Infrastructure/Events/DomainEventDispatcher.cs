// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Infrastructure.Events;

/// <summary>
/// Publishes a domain event to its listeners.
/// </summary>
/// <remarks>
/// <para>
/// The Observer pattern's subject, from Task 1 section 9.3.12.1. Resolving
/// listeners from the container rather than from a registration list kept here
/// means adding a consequence to an event is one class and one DI line — the
/// service that raises the event is never touched, which is the open/closed
/// property the pattern is for.
/// </para>
/// <para>
/// <strong>The one thing this class really does is enforce the advisory
/// contract.</strong> A listener marked <see cref="IAdvisoryListener"/> has its
/// exceptions caught and logged; any other listener's exception propagates and
/// takes the transaction with it. Without that enforcement here, "advisory" is a
/// comment on an interface rather than a behaviour — every listener would have
/// to remember to catch its own exceptions, and the one that forgot would be the
/// one that mattered.
/// </para>
/// <para>
/// Listeners run <em>sequentially, in registration order</em>. Not for
/// performance — there are at most three per event and they are all short — but
/// because two of them write to the same <c>DbContext</c>, which is not
/// thread-safe. Running them in parallel would corrupt the change tracker
/// intermittently, under load, in a way that is very hard to reproduce.
/// </para>
/// </remarks>
public sealed class DomainEventDispatcher(
    IServiceProvider services,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    public async Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var listeners = services.GetServices<IDomainEventListener<TEvent>>().ToList();

        DispatcherLog.EventDispatched(logger, domainEvent.Name, listeners.Count);

        foreach (var listener in listeners)
        {
            var isAdvisory = listener is IAdvisoryListener;

            try
            {
                await listener.HandleAsync(domainEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is not a listener failure. Swallowing it as
                // though it were would let a cancelled request quietly continue
                // through the remaining listeners.
                throw;
            }
            catch (Exception exception) when (isAdvisory)
            {
                // Advisory: recorded, not raised. A push provider being down
                // must not make a mentor standing in a hall in Sebokeng unable
                // to save a register.
                DispatcherLog.AdvisoryListenerFailed(
                    logger,
                    listener.GetType().Name,
                    domainEvent.Name,
                    exception.Message,
                    exception);
            }

            // Note the absence of a general catch. A non-advisory listener's
            // exception propagates, the unit of work rolls back, and the caller
            // hears about it — which is the intended behaviour for a badge that
            // could not be awarded or a safeguarding concern that could not be
            // routed.
        }
    }
}

/// <summary>
/// Log messages for the dispatcher.
/// </summary>
/// <remarks>
/// Declared here rather than reusing the Application layer's <c>Log</c> class,
/// which is internal to that assembly — and correctly so: a log message is part
/// of the implementation of the thing that writes it.
/// </remarks>
internal static partial class DispatcherLog
{
    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Debug,
        Message = "Domain event {EventName} dispatched to {Listeners} listener(s)")]
    public static partial void EventDispatched(ILogger logger, string eventName, int listeners);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Warning,
        Message = "Advisory listener {Listener} failed on {EventName}: {Reason} — "
            + "the transaction was not rolled back, because this listener is advisory")]
    public static partial void AdvisoryListenerFailed(
        ILogger logger,
        string listener,
        string eventName,
        string reason,
        Exception exception);
}

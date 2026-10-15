// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;
using TheeBuhle.Infrastructure.Events;

namespace TheeBuhle.UnitTests.Events;

/// <summary>
/// The Observer pattern's dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// The one thing this class really does is <strong>enforce the advisory
/// contract</strong>. A listener marked <see cref="IAdvisoryListener"/> has its
/// exceptions caught and logged; any other listener's exception propagates and
/// takes the transaction with it.
/// </para>
/// <para>
/// That enforcement has to live here rather than in each listener. If every
/// listener caught its own exceptions, the one that forgot would be the one that
/// mattered — and "advisory" would be a comment on an interface rather than a
/// behaviour. These tests are what make the word mean something.
/// </para>
/// </remarks>
public sealed class DomainEventDispatcherTests
{
    [Fact]
    public async Task Every_registered_listener_receives_the_event()
    {
        var first = new RecordingListener();
        var second = new RecordingListener();

        var dispatcher = Build(first, second);

        await dispatcher.DispatchAsync(AnEvent(), CancellationToken.None);

        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task A_dispatch_with_no_listeners_is_not_an_error()
    {
        var dispatcher = Build();

        await dispatcher.DispatchAsync(AnEvent(), CancellationToken.None);
    }

    /// <summary>
    /// A non-advisory listener's exception propagates.
    /// </summary>
    /// <remarks>
    /// This is what rolls the transaction back. The badge listener and the
    /// escalation listener both depend on it: a badge that could not be awarded
    /// leaves the learner's record wrong, and a concern that could not be routed
    /// must not leave a saved check-in behind.
    /// </remarks>
    [Fact]
    public async Task A_non_advisory_listener_failure_propagates()
    {
        var dispatcher = Build(new ThrowingListener());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(AnEvent(), CancellationToken.None));
    }

    /// <summary>
    /// An advisory listener's exception is swallowed.
    /// </summary>
    /// <remarks>
    /// The parent-notification listener is advisory, so a push provider being
    /// down must not make a mentor standing in a hall in Sebokeng unable to save
    /// a register.
    /// </remarks>
    [Fact]
    public async Task An_advisory_listener_failure_is_swallowed()
    {
        var dispatcher = Build(new ThrowingAdvisoryListener());

        await dispatcher.DispatchAsync(AnEvent(), CancellationToken.None);
    }

    /// <summary>
    /// An advisory failure does not stop the listeners after it.
    /// </summary>
    /// <remarks>
    /// The property that makes "advisory" useful rather than merely tolerant. If
    /// a failed notification stopped the chain, the learner-summary projection
    /// registered after it would silently never run — and the failure would be
    /// invisible because the request succeeded.
    /// </remarks>
    [Fact]
    public async Task An_advisory_failure_does_not_stop_the_listeners_after_it()
    {
        var after = new RecordingListener();

        var dispatcher = Build(new ThrowingAdvisoryListener(), after);

        await dispatcher.DispatchAsync(AnEvent(), CancellationToken.None);

        Assert.Equal(1, after.Calls);
    }

    /// <summary>
    /// A non-advisory failure does stop the listeners after it.
    /// </summary>
    /// <remarks>
    /// The other half, and the correct behaviour: the transaction is about to
    /// roll back, so running two more listeners that queue notifications about
    /// work that is being undone would send a parent a notice about a badge their
    /// child did not receive.
    /// </remarks>
    [Fact]
    public async Task A_non_advisory_failure_stops_the_listeners_after_it()
    {
        var after = new RecordingListener();

        var dispatcher = Build(new ThrowingListener(), after);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(AnEvent(), CancellationToken.None));

        Assert.Equal(0, after.Calls);
    }

    /// <summary>
    /// Listeners run in registration order.
    /// </summary>
    /// <remarks>
    /// Relied on deliberately: the badge listener is registered first precisely
    /// because it is the only non-advisory one on <c>AttendanceRecorded</c>, so
    /// if it is going to fail the transaction it should do so before two advisory
    /// listeners have queued notifications about work that is about to be undone.
    /// </remarks>
    [Fact]
    public async Task Listeners_run_in_registration_order()
    {
        var order = new List<string>();

        var dispatcher = Build(
            new OrderedListener("first", order),
            new OrderedListener("second", order),
            new OrderedListener("third", order));

        await dispatcher.DispatchAsync(AnEvent(), CancellationToken.None);

        Assert.Equal(["first", "second", "third"], order);
    }

    /// <summary>
    /// Cancellation is not treated as a listener failure.
    /// </summary>
    /// <remarks>
    /// Even for an advisory listener. Swallowing an
    /// <see cref="OperationCanceledException"/> as though it were a provider
    /// outage would let a cancelled request continue quietly through the
    /// remaining listeners, doing work nobody is waiting for.
    /// </remarks>
    [Fact]
    public async Task Cancellation_propagates_even_from_an_advisory_listener()
    {
        var after = new RecordingListener();

        var dispatcher = Build(new CancellingAdvisoryListener(), after);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(AnEvent(), CancellationToken.None));

        Assert.Equal(0, after.Calls);
    }

    /// <summary>
    /// Builds a dispatcher over a real service provider.
    /// </summary>
    /// <remarks>
    /// A real container rather than a fake resolver, because resolving listeners
    /// from the container <em>is</em> the mechanism under test — it is what makes
    /// adding a consequence one class and one registration line rather than an
    /// edit to the service that raises the event.
    /// </remarks>
    private static DomainEventDispatcher Build(params IDomainEventListener<TestEvent>[] listeners)
    {
        var services = new ServiceCollection();

        foreach (var listener in listeners)
        {
            services.AddSingleton(listener);
        }

        return new DomainEventDispatcher(
            services.BuildServiceProvider(),
            NullLogger<DomainEventDispatcher>.Instance);
    }

    private static TestEvent AnEvent() => new();
}

internal sealed record TestEvent : IDomainEvent
{
    public string Name => "test.event";

    public IReadOnlyDictionary<string, object?> Context => new Dictionary<string, object?>
    {
        ["kind"] = "test",
    };
}

internal sealed class RecordingListener : IDomainEventListener<TestEvent>
{
    public int Calls { get; private set; }

    public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken)
    {
        Calls++;

        return Task.CompletedTask;
    }
}

internal sealed class OrderedListener(string name, List<string> order) : IDomainEventListener<TestEvent>
{
    public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken)
    {
        order.Add(name);

        return Task.CompletedTask;
    }
}

internal sealed class ThrowingListener : IDomainEventListener<TestEvent>
{
    public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("this listener is not advisory");
}

internal sealed class ThrowingAdvisoryListener : IDomainEventListener<TestEvent>, IAdvisoryListener
{
    public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("the push provider is down");
}

/// <summary>
/// An advisory listener that cancels rather than failing.
/// </summary>
/// <remarks>
/// The distinction the dispatcher has to make: this is advisory, so a
/// naive <c>catch (Exception) when (isAdvisory)</c> would swallow the
/// cancellation too.
/// </remarks>
internal sealed class CancellingAdvisoryListener : IDomainEventListener<TestEvent>, IAdvisoryListener
{
    public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken) =>
        throw new OperationCanceledException();
}

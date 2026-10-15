// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using TheeBuhle.Domain.Common;

namespace TheeBuhle.UnitTests.Fakes;

/// <summary>
/// A clock the test controls.
/// </summary>
/// <remarks>
/// <para>
/// This type is the reason <see cref="IClock"/> exists rather than the services
/// calling <c>DateTimeOffset.Now</c> directly. Several rules in this system are
/// about time passing — a refresh token expiring, a police clearance lapsing, a
/// permission form closing, a mentor check-in going overdue — and none of them
/// can be tested against the real clock without either waiting or sleeping.
/// </para>
/// <para>
/// Frozen by default. A test that needs time to move says so, which makes the
/// passage of time an explicit step in the test rather than something that
/// happens between assertions.
/// </para>
/// </remarks>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    /// <summary>A Saturday morning in Vanderbijlpark, which is when sessions run.</summary>
    public static DateTimeOffset DefaultMoment { get; } =
        new(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(2));

    public DateTimeOffset Now { get; private set; } = now;

    public DateOnly Today => DateOnly.FromDateTime(Now.Date);

    public FakeClock()
        : this(DefaultMoment)
    {
    }

    public void Advance(TimeSpan by) => Now = Now.Add(by);

    public void AdvanceDays(int days) => Now = Now.AddDays(days);

    /// <summary>Jumps to a specific moment, for a test about a date rather than a duration.</summary>
    public void Set(DateTimeOffset moment) => Now = moment;
}

// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

namespace TheeBuhle.Domain.Common;

/// <summary>
/// Time, behind an interface.
/// </summary>
/// <remarks>
/// Attendance streaks, token expiry, mentor-clearance expiry and consent due
/// dates are all time-dependent rules. If those read <c>DateTimeOffset.UtcNow</c>
/// directly they can only be tested by waiting, so the tests would be either
/// slow or fake. With a clock, the badge-streak test can say "it is now 15
/// August 2026" and assert a definite answer.
/// <para>
/// .NET 8 introduced <see cref="TimeProvider"/>, which does the same job. This
/// interface is kept because it also pins the timezone: every date the system
/// reasons about is South African local time, and a provider that returns UTC
/// would put a Saturday 09h00 session on the wrong side of midnight for nobody's
/// benefit.
/// </para>
/// </remarks>
public interface IClock
{
    /// <summary>Now, in South African time (UTC+02:00, no daylight saving).</summary>
    DateTimeOffset Now { get; }

    /// <summary>Today's date in South African time.</summary>
    DateOnly Today { get; }
}

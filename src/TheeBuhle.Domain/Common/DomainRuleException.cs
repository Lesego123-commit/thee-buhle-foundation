// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

namespace TheeBuhle.Domain.Common;

/// <summary>
/// A business rule said no.
/// </summary>
/// <remarks>
/// Distinct from a validation failure (the input was malformed — 422) and from
/// an infrastructure error (something broke — 500). This is "the input was
/// well-formed and the answer is still no", which the API maps to 409 Conflict.
/// <para>
/// The message is written to be shown to the person who tried, so it says what
/// to do next rather than what went wrong internally.
/// </para>
/// </remarks>
public sealed class DomainRuleException(string message) : Exception(message);

/// <summary>
/// The caller is signed in but may not do this particular thing: answered 403.
/// </summary>
/// <remarks>
/// For decisions a role attribute on the endpoint cannot make, because they
/// depend on the record being acted on. Programme staff may issue a reset code
/// to a parent, for example, but not to an administrator.
/// </remarks>
public sealed class AccessDeniedException(string message) : Exception(message);

/// <summary>
/// Raised when the safeguarding escalation chain cannot route a flagged concern
/// to anybody.
/// </summary>
/// <remarks>
/// This aborts the enclosing transaction on purpose, so the check-in is not
/// saved. A mentor seeing an error and picking up the phone is a far better
/// outcome than a mentor believing a concern was reported when it reached
/// nobody. The API maps it to 503, not 500 — the request was valid and the
/// system is temporarily unable to honour a safeguarding guarantee, which is a
/// different thing from a bug.
/// </remarks>
public sealed class UnroutableConcernException(string message) : Exception(message);

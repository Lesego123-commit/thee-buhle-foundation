// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

namespace TheeBuhle.Domain.Common;

/// <summary>
/// Base for every persisted entity.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is a <see cref="Guid"/> rather than a sequential integer, and
/// the reason is stated in Task 1 section 9.3.11.4: a record identifier that
/// appears in a URL must not be guessable, or a parent who legitimately sees
/// <c>/learners/214</c> can try <c>/learners/215</c> and reach another family's
/// child.
/// </para>
/// <para>
/// The audit columns are here rather than repeated on forty entities. They are
/// stamped by <c>TheeBuhleDbContext.SaveChanges</c> so that no service can
/// forget, and <c>UpdatedBy</c> means every change to a learner's record is
/// attributable without a separate lookup.
/// </para>
/// </remarks>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The user who last changed this row. Null for system writes.</summary>
    public Guid? UpdatedBy { get; set; }
}

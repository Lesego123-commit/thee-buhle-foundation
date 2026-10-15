// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Infrastructure.Repositories;

/// <summary>
/// The audit trail.
/// </summary>
/// <remarks>
/// <para>
/// A write method and two reads, and <strong>no update or delete</strong>. That
/// absence is the design: combined with the database triggers that refuse
/// UPDATE and DELETE on the table, there are two independent reasons the trail
/// cannot be rewritten — which is the entire point of having one.
/// </para>
/// <para>
/// <c>Write</c> is synchronous and does not save. It adds to the change tracker
/// so the audit row commits in the <em>same transaction</em> as the thing it
/// describes. An audit write that committed separately could succeed while the
/// operation rolled back — recording something that never happened — or fail
/// while the operation succeeded, leaving the change unrecorded. Both are worse
/// than either failing together.
/// </para>
/// </remarks>
public sealed class AuditRepository(TheeBuhleDbContext context, IClock clock) : IAuditRepository
{
    /// <summary>
    /// Serialisation for the before and after snapshots.
    /// </summary>
    /// <remarks>
    /// No indentation, because this is machine-read and stored in millions of
    /// rows. Nulls are kept: "the field was set to null" is a change worth
    /// recording, and omitting it would make an audit entry ambiguous between
    /// "cleared" and "untouched".
    /// </remarks>
    private static readonly JsonSerializerOptions Snapshot = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,

        // Enums as their NAMES, for the same reason the database columns store
        // them as text — and this was wrong until a real audit row was read
        // back. It said {"wellbeing":3,"mode":1}, which needs the C# enum to
        // interpret and, worse, silently changes meaning if a member is ever
        // inserted or reordered.
        //
        // An audit trail is the one table in this system that must still be
        // readable and still mean the same thing in five years, after the code
        // has moved on. {"wellbeing":"Concerned"} is self-describing; 3 is a
        // reference to a version of an enum nobody has kept.
        Converters = { new JsonStringEnumConverter() },
    };

    public void Write(
        Guid? userId,
        string action,
        string entityName,
        string? entityId,
        object? before,
        object? after,
        string ipAddress,
        string userAgent)
    {
        context.AuditLog.Add(new AuditLogEntry
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            BeforeJson = Serialise(before),
            AfterJson = Serialise(after),
            IpAddress = ipAddress,
            // Truncated rather than allowed to fail the insert. A browser sending
            // an absurdly long user-agent must not be able to stop an audit row
            // being written — which would mean stopping the operation it
            // describes.
            UserAgent = Truncate(userAgent, 255),
            OccurredAt = clock.Now,
        });
    }

    /// <summary>
    /// The recent trail, for the administrator's console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note what the projection carries and what it does not. It carries the
    /// action, the entity, the actor's email, the address and the time — enough
    /// to answer "who did what, when, from where". It does <strong>not</strong>
    /// carry the before and after snapshots.
    /// </para>
    /// <para>
    /// That is deliberate: the snapshots contain whatever the audited operation
    /// touched, which for a learner update includes a child's name and school.
    /// Putting them on a paged list would render dozens of children's details on
    /// one screen. They are readable per-entity, where somebody has already
    /// established they may see that record.
    /// </para>
    /// </remarks>
    public async Task<PagedResult<AuditView>> RecentAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var total = await context.AuditLog.AsNoTracking().CountAsync(cancellationToken);

        var items = await context.AuditLog
            .AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new AuditView(
                entry.Id,
                entry.Action,
                entry.EntityName,
                entry.EntityId,
                // Looked up rather than stored. The actor id is kept as a plain
                // value with no foreign key, so a deleted account leaves the
                // entry intact with an unresolved actor — which is the right
                // outcome for a trail that must survive the people in it.
                context.Users
                    .Where(user => user.Id == entry.UserId)
                    .Select(user => user.Email)
                    .FirstOrDefault(),
                entry.IpAddress,
                entry.OccurredAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditView>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<AuditView>> ForEntityAsync(
        string entityName,
        string entityId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await context.AuditLog
            .AsNoTracking()
            .Where(entry => entry.EntityName == entityName && entry.EntityId == entityId)
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(limit)
            .Select(entry => new AuditView(
                entry.Id,
                entry.Action,
                entry.EntityName,
                entry.EntityId,
                context.Users
                    .Where(user => user.Id == entry.UserId)
                    .Select(user => user.Email)
                    .FirstOrDefault(),
                entry.IpAddress,
                entry.OccurredAt))
            .ToListAsync(cancellationToken);

    private static string? Serialise(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, Snapshot);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

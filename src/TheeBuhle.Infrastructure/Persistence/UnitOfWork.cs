// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Infrastructure.Persistence;

/// <summary>
/// The Unit of Work, over one <see cref="TheeBuhleDbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's change tracker already <em>is</em> a unit of work, so this class
/// exists for one reason the tracker does not cover: <strong>nesting</strong>.
/// Services call other services — <c>AnnouncementService.PublishPermissionFormAsync</c>
/// calls <c>PublishAsync</c>, and both ask for a transaction — and the naive
/// implementation opens a second transaction whose commit releases the first.
/// The result is a partial commit that looks like a success.
/// </para>
/// <para>
/// So a nested call joins the outer transaction instead. Only the outermost
/// frame commits, and any exception anywhere inside rolls the whole thing back.
/// That is the guarantee the safeguarding path depends on: if the escalation
/// chain cannot route a concern, the check-in must not be saved, and it is the
/// outer transaction that has to disappear, not just the inner one.
/// </para>
/// </remarks>
public sealed class UnitOfWork(TheeBuhleDbContext context) : IUnitOfWork
{
    /// <summary>
    /// The transaction this unit of work opened, if it is the outermost frame.
    /// </summary>
    /// <remarks>
    /// Null while no transaction is in flight. Non-null means a nested call must
    /// join rather than begin.
    /// </remarks>
    private IDbContextTransaction? current;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Runs the work inside a transaction, committing on success and rolling
    /// back on any exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>ExecuteAsync</c> wrapper is not optional and is easy to leave out.
    /// Azure SQL needs <c>EnableRetryOnFailure</c> — the service relocates
    /// databases for maintenance and failover, which surfaces as a dropped
    /// connection — and EF refuses to let a retrying execution strategy coexist
    /// with a hand-started transaction unless the whole transaction is inside
    /// the strategy.
    /// </para>
    /// <para>
    /// The reason it refuses is sound: if a commit failed transiently, a retry
    /// that resumed halfway through would commit part of the work twice. Putting
    /// the <em>whole</em> transaction inside the strategy means a transient
    /// failure re-runs all of it against a fresh connection, which is the only
    /// safe way to retry a transaction.
    /// </para>
    /// <para>
    /// This was found by running the application rather than by reading it. The
    /// combination throws <c>InvalidOperationException</c> on the first
    /// transactional write — which is very nearly every write in the system —
    /// so without the wrapper nothing that changes data works at all.
    /// </para>
    /// </remarks>
    public async Task<T> TransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Already inside one: run the work and let the outer frame decide the
        // outcome. Deliberately does not commit — committing here is exactly the
        // bug this class exists to prevent — and deliberately does not open a
        // second execution strategy, because the outer one already owns the
        // retry.
        if (current is not null)
        {
            return await work().ConfigureAwait(false);
        }

        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            current = transaction;

            try
            {
                var result = await work().ConfigureAwait(false);

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

                // The tracker still holds the entities the failed work added, and
                // this context may be reused for the rest of the request — so a
                // later save would silently retry the rolled-back writes outside
                // a transaction. Detaching them is what makes the rollback
                // complete, and it is also what makes a retry of this whole
                // delegate start from a clean tracker rather than from the
                // half-built state the failed attempt left behind.
                DiscardTrackedChanges();

                throw;
            }
            finally
            {
                current = null;
            }
        }).ConfigureAwait(false);
    }

    public async Task TransactionAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await TransactionAsync<object?>(async () =>
        {
            await work().ConfigureAwait(false);

            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the change tracker to the state it was in before the failed work.
    /// </summary>
    /// <remarks>
    /// Added entities are detached; modified and deleted ones are reverted to
    /// their original values. Without this, a rolled-back check-in would still
    /// be sitting in the tracker as Added, and the next save on this context
    /// would insert it — after the transaction that was supposed to prevent
    /// exactly that had already rolled back.
    /// </remarks>
    private void DiscardTrackedChanges()
    {
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;

                default:
                    break;
            }
        }
    }
}

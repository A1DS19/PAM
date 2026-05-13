using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Pam.Shared.Data.Interceptors;

// Enrols every business DbContext onto the request's shared transaction so
// business writes and the outbox write commit atomically. Cross-context
// atomicity follow-up from ADR #26 — closes the under-deliver window between
// business SaveChanges and outbox SaveChanges.
//
// When AmbientTransaction.Current is null (queries, migrations, seeders,
// hosted services) this is a no-op.
//
// Pattern note: shared SqlConnection + UseTransaction is an EF Core idiom
// but not blessed by MassTransit's outbox docs; the EF outbox doesn't care
// HOW SaveChangesAsync runs as long as it does. Verified end-to-end via
// AtomicOutboxTests.
public sealed class AmbientTransactionInterceptor(AmbientTransaction ambient)
    : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        await EnrolAsync(eventData, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        // Synchronous saves still enrol; UseTransaction is cheap.
        if (
            ambient.Current is { } txn
            && eventData.Context is { } ctx
            && ctx.Database.CurrentTransaction is null
        )
        {
            ctx.Database.UseTransaction(txn.GetDbTransaction());
        }
        return base.SavingChanges(eventData, result);
    }

    private async ValueTask EnrolAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken
    )
    {
        if (ambient.Current is not { } txn || eventData.Context is not { } ctx)
        {
            return;
        }

        // Idempotent: if this context is already on the shared txn (because
        // an earlier SaveChanges in the same request already enrolled it),
        // skip. CurrentTransaction is the same instance across calls.
        if (ReferenceEquals(ctx.Database.CurrentTransaction, txn))
        {
            return;
        }

        await ctx.Database.UseTransactionAsync(txn.GetDbTransaction(), cancellationToken);
    }
}

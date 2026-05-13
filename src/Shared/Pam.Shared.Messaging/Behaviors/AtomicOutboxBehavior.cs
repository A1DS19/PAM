using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Data;
using Pam.Shared.Messaging.Data;

namespace Pam.Shared.Messaging.Behaviors;

// Single-transaction wrapper around every command. Replaces OutboxFlushBehavior
// (which committed business + outbox in separate transactions and left a
// sub-millisecond under-deliver window — see ADR #26).
//
// Flow:
//
//   1. Behavior begins a transaction on PamMessagingDbContext at the start of
//      every command. The IDbContextTransaction wraps a SqlConnection owned
//      by the messaging context.
//
//   2. The txn is stashed in the scoped AmbientTransaction service.
//
//   3. Handler runs. Inside the handler:
//        - DispatchDomainEventsInterceptor (pre-save) fires bridge handlers
//          that call IPublishEndpoint.Publish — OutboxMessage rows queue in
//          messaging context's change tracker (MT's bus context provider).
//          Bridge handlers also Add() an OutboxDispatchedLog row to the
//          messaging context so the reconciler can detect orphans later.
//        - AmbientTransactionInterceptor.SavingChangesAsync runs on the
//          business context, calls UseTransactionAsync(ambientTxn) →
//          business context switches to the shared SqlConnection.
//        - Business INSERT/UPDATE runs on that connection inside the shared txn.
//
//   4. Handler returns. Behavior calls messaging.SaveChangesAsync —
//      OutboxMessage + OutboxDispatchedLog rows INSERT on the same shared
//      connection inside the shared txn.
//
//   5. txn.CommitAsync — single COMMIT durably persists every row from
//      step 3 and step 4 atomically. Crash anywhere before this rolls
//      back the whole request.
//
// After commit, BusOutboxNotification wakes the EntityFrameworkOutboxDeliveryService;
// the new rows dispatch to RabbitMQ sub-second. A crash between commit and
// dispatch is harmless — the row sits in messaging.outbox_message until the
// delivery service retries.
//
// Pattern footprint: shared SqlConnection + UseTransaction across two
// DbContexts is an EF Core idiom; MT 8.5 does not officially endorse it (one
// outbox-owning DbContext per bus is the documented constraint), but the EF
// outbox doesn't care HOW SaveChangesAsync runs as long as it does. Verified
// end-to-end in AtomicOutboxTests. Off the table: MT 9.1's per-DbContext
// outbox overload (license; ADR #5) and System.Transactions.TransactionScope
// (explicitly incompatible with the EF outbox — MT discussion #4972).
//
// Pipeline position: innermost behavior, after Audit, so AuditBehavior records
// the success/failure of the entire transactional unit (including the commit).
public sealed class AtomicOutboxBehavior<TRequest, TResponse>(
    PamMessagingDbContext messaging,
    AmbientTransaction ambient,
    ILogger<AtomicOutboxBehavior<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        // Queries skip the txn — nothing publishes, nothing to commit.
        if (!IsCommand(request))
        {
            return await next();
        }

        // Reentry guard: if a command-from-a-command path appears (nested
        // ISender.Send) the outer txn already covers the inner work.
        if (ambient.IsActive)
        {
            return await next();
        }

        await using var txn = await messaging.Database.BeginTransactionAsync(cancellationToken);
        ambient.Set(txn);

        try
        {
            var response = await next();

            if (messaging.ChangeTracker.HasChanges())
            {
                var written = await messaging.SaveChangesAsync(cancellationToken);
                logger.LogDebug(
                    "Flushed {Count} messaging row(s) inside atomic txn for {Request}",
                    written,
                    typeof(TRequest).Name
                );
            }

            await txn.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            // Best-effort rollback. If the connection itself is gone the
            // server already aborted the txn; swallowing here avoids
            // masking the original exception.
            try
            {
                await txn.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                logger.LogWarning(
                    rollbackEx,
                    "Rollback failed for {Request} — the original exception is being rethrown",
                    typeof(TRequest).Name
                );
            }
            throw;
        }
        finally
        {
            ambient.Clear();
        }
    }

    private static bool IsCommand(TRequest request) =>
        request is ICommand
        || request
            .GetType()
            .GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
}

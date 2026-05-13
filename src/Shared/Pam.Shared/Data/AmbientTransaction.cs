using Microsoft.EntityFrameworkCore.Storage;

namespace Pam.Shared.Data;

// Scoped, per-request holder for the request's shared IDbContextTransaction.
// AtomicOutboxBehavior opens the txn on PamMessagingDbContext at the start of
// every command and stashes it here. Every business module's
// AmbientTransactionInterceptor reads from here in SavingChangesAsync and
// enrolls its context onto the same txn via Database.UseTransactionAsync —
// so business rows and outbox rows commit in a single transaction on a
// single SqlConnection.
//
// Null outside a command scope (queries, migrations, seeders, hosted
// services) — the interceptor no-ops, callers behave as before.
public sealed class AmbientTransaction
{
    public IDbContextTransaction? Current { get; private set; }

    public bool IsActive => Current is not null;

    public void Set(IDbContextTransaction txn) => Current = txn;

    public void Clear() => Current = null;
}

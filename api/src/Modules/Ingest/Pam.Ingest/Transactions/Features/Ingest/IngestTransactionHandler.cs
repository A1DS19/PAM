using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Data;
using Pam.Ingest.Transactions.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.Time;

namespace Pam.Ingest.Transactions.Features.Ingest;

public sealed class IngestTransactionHandler(IngestDbContext db, IClock clock)
    : ICommandHandler<IngestTransactionCommand, IngestTransactionResult>
{
    // SQL Server error numbers for unique-key violations. 2627 is the
    // generic UNIQUE constraint violation; 2601 is the equivalent for a
    // UNIQUE index. Both fire on the same INSERT collision pattern; catch
    // both so the duplicate-vendor-retry path stays correct regardless of
    // whether the idempotency rule is enforced by a constraint or an
    // index. (Postgres collapsed both into SQLSTATE 23505; SQL Server
    // splits them.)
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public async Task<IngestTransactionResult> Handle(
        IngestTransactionCommand cmd,
        CancellationToken cancellationToken
    )
    {
        var receivedAt = clock.UtcNow;

        var tx = VendorTransaction.Record(
            id: PamIds.New(),
            vendorId: cmd.VendorId,
            vendorReference: cmd.VendorReference,
            brandId: cmd.BrandId,
            playerId: cmd.PlayerId,
            amountCents: cmd.AmountCents,
            currency: cmd.Currency,
            kind: cmd.Kind,
            occurredAt: cmd.OccurredAt,
            receivedAt: receivedAt,
            roundId: cmd.RoundId,
            description: cmd.Description
        );

        db.VendorTransactions.Add(tx);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Insert-first idempotency path: duplicate retries hit the
            // UNIQUE index and we return the already-persisted winner.
            db.ChangeTracker.Clear();

            var raced = await db
                .VendorTransactions.AsNoTracking()
                .Where(t => t.VendorId == cmd.VendorId && t.VendorReference == cmd.VendorReference)
                .Select(t => new { t.Id })
                .FirstAsync(cancellationToken);

            return new IngestTransactionResult(raced.Id, TransactionStatus.Duplicate);
        }

        return new IngestTransactionResult(tx.Id, TransactionStatus.Received);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql
        && (sql.Number == UniqueConstraintViolation || sql.Number == UniqueIndexViolation);
}

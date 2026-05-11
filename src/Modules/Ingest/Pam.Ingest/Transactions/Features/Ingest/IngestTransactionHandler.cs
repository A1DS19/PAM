using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    // Postgres SQLSTATE for unique_violation. We catch this exactly to
    // distinguish "vendor retried with same reference" (TransactionStatus
    // .Duplicate, success) from genuine errors.
    private const string UniqueViolationSqlState = "23505";

    public async Task<IngestTransactionResult> Handle(
        IngestTransactionCommand cmd,
        CancellationToken cancellationToken
    )
    {
        // Fast-path duplicate check — short-circuits the common case
        // without paying the DB round-trip cost of an insert + rollback.
        // The UNIQUE constraint catches the rare race where two replicas
        // process the same vendor retry concurrently.
        var existing = await db
            .VendorTransactions.AsNoTracking()
            .Where(t => t.VendorId == cmd.VendorId && t.VendorReference == cmd.VendorReference)
            .Select(t => new { t.Id, t.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return new IngestTransactionResult(existing.Id, TransactionStatus.Duplicate);
        }

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
            // Concurrent vendor retry hit the UNIQUE index between our
            // fast-path check and the insert. Re-fetch and return the
            // winning row's id.
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
        ex.InnerException is PostgresException pg
        && string.Equals(pg.SqlState, UniqueViolationSqlState, StringComparison.Ordinal);
}

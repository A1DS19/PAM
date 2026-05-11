using Pam.Shared.Contracts.Identity;
using Pam.Shared.DDD;

namespace Pam.Wallet.Accounts.Models;

// Scaffold-only aggregate. The real wallet model is a double-entry
// ledger: paired (debit, credit) entries inside a Transaction that
// sums to zero per currency, atomic with the LedgerEntryPosted
// integration event via the outbox. That's the next PR's work.
//
// Account here exists so the migration has a real table to create and
// so brand-scoped tenancy gets stamped on day one (BrandId, PlayerId).
public sealed class Account : Aggregate<Guid>
{
    public Guid BrandId { get; private set; }

    public Guid PlayerId { get; private set; }

    public string Currency { get; private set; } = default!;

    private Account() { }

    public static Account Open(Guid brandId, Guid playerId, string currency)
    {
        return new Account
        {
            Id = PamIds.New(),
            BrandId = brandId,
            PlayerId = playerId,
            Currency = currency,
        };
    }
}

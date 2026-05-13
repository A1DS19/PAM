namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// Phase-A implementation of `GetCustomerBalance21GCasino.asmx`. Read-only
// balance query — DO NOT persist a transaction.
//
// In Phase A the wallet lives in GBS, not PAM, so the only real source
// of truth is GBS. Without IGbsRelay we can't return a real balance; the
// stub returns 0 so 21G's client sees a syntactically-valid response
// during development. Production traffic stays on GBS until the
// forwarder ships.
public sealed class TwentyOneGGetBalanceService : ITwentyOneGGetBalanceService
{
    public Task<TransactionResult> PostTransaction(
        string? systemID,
        string? systemPassword,
        string? clerkID,
        string? customerID,
        string? amount,
        string? tranCode,
        string? tranType,
        string? description,
        string? bettingAdjustmentFlagYN,
        string? dailyFigureDate_YYYYMMDD,
        string? enteredBy,
        string? paymentBy
    )
    {
        return Task.FromResult(
            new TransactionResult
            {
                DocumentNumber = 0,
                RespMessage = "OK (PAM Phase A — balance lookup stubbed)",
                AvailableBalance = 0,
            }
        );
    }
}

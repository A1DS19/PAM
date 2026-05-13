namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// Phase-A implementation of `ValidateSessionID21GCasino.asmx`. The WSDL
// schema is identical to CustomerTransaction, but the semantics are
// read-only: validate that the given (customerID, session) is current,
// return a status — DO NOT persist a transaction.
//
// In Phase A we don't yet have session state in PAM (it lives in GBS).
// Without the IGbsRelay forwarder, we can't actually validate; this
// stub returns "OK" so 21G's client gets a syntactically-valid response
// during development. Production traffic continues hitting GBS directly
// until the forwarder ships.
public sealed class TwentyOneGValidateSessionService : ITwentyOneGValidateSessionService
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
                RespMessage = "OK (PAM Phase A — session lookup stubbed)",
                AvailableBalance = 0,
            }
        );
    }
}

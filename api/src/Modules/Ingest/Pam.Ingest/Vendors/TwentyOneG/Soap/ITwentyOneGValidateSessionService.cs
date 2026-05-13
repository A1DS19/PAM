using System.ServiceModel;

namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// SoapCore service contract for `ValidateSessionID21GCasino.asmx`. WSDL
// schema is identical to CustomerTransaction21G — same `PostTransaction`
// envelope, same 12 fields, same TransactionResult. Behavior differs at
// the implementation layer (read-only session check; no row write).
[ServiceContract(Namespace = TwentyOneGSoapDefaults.Namespace)]
public interface ITwentyOneGValidateSessionService
{
    [OperationContract]
    Task<TransactionResult> PostTransaction(
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
    );
}

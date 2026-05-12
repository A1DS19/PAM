using System.ServiceModel;

namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// SoapCore service contract for `GetCustomerBalance21GCasino.asmx`. WSDL
// schema is identical to the other two — same `PostTransaction` envelope,
// same 12 fields, same TransactionResult. Behavior at the implementation
// layer: read-only balance query (no row write).
[ServiceContract(Namespace = TwentyOneGSoapDefaults.Namespace)]
public interface ITwentyOneGGetBalanceService
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

using System.ServiceModel;

namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// SoapCore service contract for `CustomerTransaction21G.asmx`. The WSDL
// (snapshot at `infra/wsdl/21g/CustomerTransaction21G.wsdl`) advertises a
// single `PostTransaction` operation with 12 string parameters.
//
// Parameter names match the WSDL element names CHARACTER-FOR-CHARACTER
// (case + underscores included). Changing any of these breaks 21G's
// existing client — it builds the SOAP envelope from these names.
[ServiceContract(Namespace = TwentyOneGSoapDefaults.Namespace)]
public interface ITwentyOneGCustomerTransactionService
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

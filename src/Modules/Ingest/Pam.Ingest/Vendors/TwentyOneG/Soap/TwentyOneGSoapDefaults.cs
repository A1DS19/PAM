namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// Constants pulled from the production WSDL snapshot in
// `infra/wsdl/21g/`. Centralized so a future drift detection diff can
// compare against fixed values instead of magic strings sprinkled
// across multiple service contracts.
internal static class TwentyOneGSoapDefaults
{
    // The XML target namespace from every PostTransaction WSDL. ASP.NET
    // Web Services' default — GBS never customized it. Matching this
    // EXACTLY is what makes 21G's existing client think we're the same
    // service it always called.
    public const string Namespace = "http://tempuri.org/";

    // The three route paths PAM exposes, mirroring GBS's `/integrations/
    // 21GCasino/<service>.asmx` URL structure. 21G's config change to
    // route to us is purely the host portion.
    public const string CustomerTransactionPath =
        "/integrations/21GCasino/CustomerTransaction21G.asmx";

    public const string ValidateSessionPath =
        "/integrations/21GCasino/ValidateSessionID21GCasino.asmx";

    public const string GetBalancePath = "/integrations/21GCasino/GetCustomerBalance21GCasino.asmx";
}

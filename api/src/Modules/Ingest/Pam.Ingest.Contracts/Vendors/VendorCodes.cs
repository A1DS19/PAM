namespace Pam.Ingest.Contracts.Vendors;

// Well-known vendor identifiers, stored verbatim in
// ingest.vendor_transactions.vendor_id. Adding a vendor here is the contract;
// the adapter implementation in Pam.Ingest/Vendors/<X>/ uses these strings
// to register routes and to discriminate rows.
//
// Lowercase, no spaces — these flow into URL path segments
// (/v1/ingest/vendors/<code>) and SQL Server lookups.
public static class VendorCodes
{
    public const string TwentyOneG = "21g";
    public const string FastSpin = "fastspin";
    public const string BTCasino = "btcasino";
    public const string Vegas = "vegas";
    public const string WNet = "wnet";
    public const string Pocket = "pocket";
}

namespace Pam.Ingest.Transactions.Exceptions;

// Stable error codes carried in ProblemDetails.extensions.code. Clients
// program against these strings; messages can localize freely.
public static class IngestErrors
{
    public const string Duplicate = "ingest.transaction.duplicate";
    public const string UnknownVendor = "ingest.vendor.unknown";
    public const string UnknownPlayer = "ingest.player.unknown";
    public const string InvalidCurrency = "ingest.currency.invalid";
    public const string InvalidAmount = "ingest.amount.invalid";
    public const string VendorAuthFailed = "ingest.vendor.auth-failed";
}

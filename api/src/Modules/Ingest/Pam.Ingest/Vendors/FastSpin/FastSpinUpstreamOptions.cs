namespace Pam.Ingest.Vendors.FastSpin;

public sealed class FastSpinUpstreamOptions
{
    public const string SectionName = "Ingest:Vendors:FastSpin";

    // Where GBS's /api/fastspin/main lives. Bound from appsettings or env;
    // no default — a missing config should fail-fast at startup rather
    // than silently route to the wrong place.
    public string UpstreamUrl { get; init; } = "";

    // Per-request budget for the forward to GBS. Vendor-side typically
    // expects sub-second responses on getBalance / transfer; 30s gives
    // plenty of headroom while still letting us flag UpstreamTimeout
    // before the vendor's own timeout kicks in.
    public int TimeoutSeconds { get; init; } = 30;
}

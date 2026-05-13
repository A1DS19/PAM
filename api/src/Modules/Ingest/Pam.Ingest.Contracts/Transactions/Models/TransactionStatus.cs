namespace Pam.Ingest.Contracts.Transactions.Models;

// Lifecycle state of an ingested vendor transaction.
//
// During Phase A of the strangler migration (intercept-and-forward),
// every successful ingest lands as Received and stays there — GBS owns
// posting. In Phase C+, PAM transitions Received → Posted itself.
public enum TransactionStatus
{
    /// Accepted, persisted, integration event published. Default terminal
    /// state during Phase A; transient state in Phase C+ pending wallet
    /// authorization.
    Received,

    /// Wallet authorized + applied. Reachable only when PAM owns the
    /// wallet (Phase C+).
    Posted,

    /// (vendor_id, vendor_reference) already exists. Idempotent re-submit
    /// from the vendor — return success with existing transaction.
    Duplicate,

    /// Validation failed (unknown player, unknown currency, disabled
    /// account, etc.). The row is still persisted as an audit record but
    /// no balance changed.
    Rejected,
}

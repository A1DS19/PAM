namespace Pam.Ingest.Contracts.Transactions.Models;

// What a vendor transaction represents in business terms.
// Stored as string in vendor_transactions.kind for column self-documentation.
public enum TransactionKind
{
    /// A bet stake — debits the player's balance.
    Risk,

    /// A payout — credits the player's balance.
    Win,

    /// A reversal of a prior Risk or Win (cancelled bet, mistaken settlement).
    Refund,

    /// A bonus credit applied through the vendor (free spin awards, etc.).
    Bonus,

    /// Manual adjustment / correction. Operator-initiated, not vendor-initiated.
    Correction,
}

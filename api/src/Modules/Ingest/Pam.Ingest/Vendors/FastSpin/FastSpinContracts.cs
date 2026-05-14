namespace Pam.Ingest.Vendors.FastSpin;

// Wire shapes for the FastSpin (Kingdom Casino) intercept path. PAM sits
// transparently between FastSpin and GBS: FastSpin POSTs the JSON body
// here, we forward unchanged to GBS, and we parse BOTH this body and
// GBS's response to build the canonical IngestTransactionCommand.
//
// The shapes mirror GBS's own DTOs in GBSApi.Models.FastSpin (the
// reference for what FastSpin currently sends), so PAM never needs to
// transform the body — we forward it byte-for-byte, then parse a copy.
// MD5(body + securityKey) Digest validation happens upstream at GBS;
// PAM is intentionally transparent.

// Common envelope fields every FastSpin call carries.
public abstract record FastSpinGeneralRequest
{
    public string? serialNo { get; init; }
    public string? merchantCode { get; init; }
}

// `transfer` body — covers place-bet (type=1), cancel-bet (type=2),
// payout (type=4), bonus (type=7). type is the discriminator.
public sealed record FastSpinTransferRequest : FastSpinGeneralRequest
{
    public string? transferId { get; init; }
    public string? acctId { get; init; }
    public string? currency { get; init; }
    public double amount { get; init; }
    public int type { get; init; }
    public string? channel { get; init; }
    public string? gameCode { get; init; }
    public string? ticketId { get; init; }
    public string? referenceId { get; init; }
    public FastSpinSpecialGame? specialGame { get; init; }
    public IReadOnlyList<string>? refTicketIds { get; init; }
    public string? playerIp { get; init; }
    public string? gameFeature { get; init; }
    public string? transferTime { get; init; }
}

public sealed record FastSpinSpecialGame(string? type, int count, int sequence);

// GBS's TransferResponse — the fields PAM captures into the downstream_*
// columns. Note `balance` is a double on the wire (a money value scaled
// 1:1 to currency units, e.g. 1050.15 = €1050.15); we multiply by 100 to
// store as signed cents alongside AmountCents.
//
// `balance` is nullable on purpose. On vendor-level rejections (e.g.
// code=1 "Invalid acctId") GBS replies with a GeneralResp shape that
// omits balance entirely; the JSON deserializer would otherwise leave
// it at 0.0, and we'd persist "the player has $0" — which is a lie.
// Nullable means a missing field stays null and the captured row tells
// the truth: we don't know the balance.
public sealed record FastSpinTransferResponse
{
    public string? transferId { get; init; }
    public string? merchantTxId { get; init; }
    public string? acctId { get; init; }
    public double? balance { get; init; }
    public int code { get; init; }
    public string? msg { get; init; }
    public string? serialNo { get; init; }
    public string? merchantCode { get; init; }
}

// `getBalance` body — query only, no transaction to persist. PAM forwards
// + relays without writing a row.
public sealed record FastSpinGetBalanceRequest : FastSpinGeneralRequest
{
    public string? acctId { get; init; }
    public string? gameCode { get; init; }
}

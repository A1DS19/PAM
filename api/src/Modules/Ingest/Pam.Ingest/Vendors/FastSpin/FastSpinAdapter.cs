using System.Text.Json;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Contracts.Vendors;
using Pam.Ingest.Transactions.Features.Ingest;

namespace Pam.Ingest.Vendors.FastSpin;

// Bridges FastSpin's wire shape to PAM's canonical IngestTransactionCommand.
// Used only on the `transfer` verb — getBalance carries no money movement
// and is forwarded without persistence.
//
// This is deliberately NOT an IVendorAdapter implementation. IVendorAdapter
// is the shape for "PAM owns the response" (Phase C). FastSpin is Phase A
// intercept: GBS owns the response, PAM captures both directions.
//
// Extracts both halves into a single command:
//   - Request side: transferId (idempotency), acctId → PlayerId, amount,
//     gameCode, etc.
//   - Response side: GBS's balance + merchantTxId + code/msg, plus the
//     overall outcome from the forward attempt (Forwarded / UpstreamError
//     / UpstreamTimeout / UpstreamUnreachable).
public static class FastSpinAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Builds a command from the captured request + response bytes. Returns
    // null if the request body isn't a recognizable transfer (e.g. malformed
    // JSON, or a transfer type we don't model) — the endpoint relays the
    // upstream response anyway, just doesn't persist.
    //
    // Static today because no per-request services are needed. Becomes
    // an instance method once Pam.Players ships IPlayerLookup (vendor
    // acctId → PAM PlayerId) — at which point this gets injected.
    public static IngestTransactionCommand? ExtractCommand(
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> responseBody,
        DownstreamStatus outcome,
        int latencyMs
    )
    {
        FastSpinTransferRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<FastSpinTransferRequest>(requestBody, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (
            request is null
            || string.IsNullOrEmpty(request.transferId)
            || string.IsNullOrEmpty(request.acctId)
        )
        {
            return null;
        }

        var kind = MapTransferType(request.type);
        if (kind is null)
        {
            // Unknown transfer type — relay upstream's verdict, skip persistence.
            return null;
        }

        // FastSpin sends amount as a positive double in currency units
        // (e.g. 2.50 = €2.50). Canonical storage is signed cents:
        // bets/refunds negative, payouts/bonuses positive. The transfer
        // type drives the sign — vendors are inconsistent here so we
        // never trust the sign on the wire.
        var amountCents = ToCents(request.amount);
        amountCents = kind == TransactionKind.Risk ? -Math.Abs(amountCents) : Math.Abs(amountCents);

        // Best-effort parse of GBS's response. Only attempt the parse
        // when we got a CLEAN response (Forwarded). For UpstreamError /
        // Timeout / Unreachable the body — if any — is HTTP-infrastructure
        // error text (IIS HTML, 415 JSON envelope, etc.), not a FastSpin
        // response. Deserializing those as FastSpinTransferResponse
        // would succeed with all-default values and silently lie in the
        // DB (e.g. code=0 looks like "Success" but the call failed).
        FastSpinTransferResponse? response = null;
        if (outcome == DownstreamStatus.Forwarded && responseBody.Length > 0)
        {
            try
            {
                response = JsonSerializer.Deserialize<FastSpinTransferResponse>(
                    responseBody,
                    JsonOptions
                );
            }
            catch (JsonException)
            {
                // Leave response = null; downstream_* columns stay null
                // except status + latency which we know unconditionally.
            }
        }

        // GBS player IDs map 1:1 to a PAM PlayerId via IPlayerLookup
        // (Pam.Players, not yet shipped). For Phase A we use a deterministic
        // placeholder so the row commits — the real lookup lands when
        // Pam.Players exposes IPlayerLookup. Same Phase-A placeholder pattern
        // as TwentyOneGAdapter.
        var playerId = DerivePlaceholderGuid(request.acctId);
        var brandId = DerivePlaceholderGuid("default-brand");

        // FastSpin's `transferTime` is yyyymmddTHHmmss (vendor's clock).
        // If parseable, use as the business OccurredAt; otherwise the
        // handler's IClock.UtcNow lands as ReceivedAt and OccurredAt
        // defaults to the same.
        var occurredAt = TryParseTransferTime(request.transferTime) ?? DateTimeOffset.UtcNow;

        return new IngestTransactionCommand(
            VendorId: VendorCodes.FastSpin,
            VendorReference: request.transferId!,
            BrandId: brandId,
            PlayerId: playerId,
            AmountCents: amountCents,
            Currency: NormalizeCurrency(request.currency),
            Kind: kind.Value,
            OccurredAt: occurredAt,
            RoundId: request.referenceId,
            Description: request.gameCode,
            VendorBalanceAfterCents: response is null ? null : ToCents(response.balance),
            DownstreamReference: response?.merchantTxId,
            DownstreamOutcomeCode: response?.code,
            DownstreamOutcomeMessage: response?.msg,
            DownstreamStatus: outcome,
            DownstreamLatencyMs: latencyMs
        );
    }

    private static TransactionKind? MapTransferType(int type) =>
        type switch
        {
            1 => TransactionKind.Risk, // place bet
            2 => TransactionKind.Risk, // cancel bet — modeled as a refund of the original Risk;
            // sign-flip handled by the caller via Math.Abs negation.
            4 => TransactionKind.Win, // payout
            7 => TransactionKind.Bonus, // bonus payout
            _ => null,
        };

    private static long ToCents(double currencyUnits) =>
        (long)Math.Round(currencyUnits * 100, MidpointRounding.AwayFromZero);

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrEmpty(currency) ? "USD" : currency.ToUpperInvariant();

    private static DateTimeOffset? TryParseTransferTime(string? transferTime)
    {
        if (string.IsNullOrEmpty(transferTime))
        {
            return null;
        }
        // "20120720T230043"  → 2012-07-20 23:00:43 UTC
        return DateTimeOffset.TryParseExact(
            transferTime,
            "yyyyMMddTHHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dto
        )
            ? dto.ToUniversalTime()
            : null;
    }

    // Phase-A placeholder GUID derivation. Replaced by IPlayerLookup
    // (Pam.Players) when that ships. Deterministic so retries map to
    // the same row. SHA256 (not SHA1) — there's no security boundary
    // here, but CA5350 flags SHA1 anywhere and the cost is identical.
    private static Guid DerivePlaceholderGuid(string source)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(source)
        );
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }
}

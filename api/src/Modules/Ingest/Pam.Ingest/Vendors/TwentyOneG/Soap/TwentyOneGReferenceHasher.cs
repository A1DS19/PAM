using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// 21G's `PostTransaction` envelope does not include a `reference` field
// (no idempotency key on the wire). GBS itself dedupes via a `Reference`
// column on `tbCasinoPlayToday`, which must therefore be derived
// internally from the request payload — we mirror that.
//
// We hash the fields that uniquely identify a transaction:
//   systemID + customerID + dailyFigureDate + amount + tranCode +
//   tranType + description
//
// An exact replay from 21G produces the same hash → our UNIQUE constraint
// on (vendor_id, vendor_reference) catches it and we surface
// TransactionStatus.Duplicate without writing a second row.
//
// Two semantically-different transactions with identical fields would
// false-positive as duplicates, but `description` and `tranCode` make
// that essentially impossible in practice.
internal static class TwentyOneGReferenceHasher
{
    public static string ComputeReference(
        string? systemId,
        string? customerId,
        string? dailyFigureDate,
        string? amount,
        string? tranCode,
        string? tranType,
        string? description
    )
    {
        // Canonical join — fixed order, fixed separator, empty for null.
        // Same inputs always produce the same canonical string.
        var canonical = string.Join(
            '|',
            systemId ?? string.Empty,
            customerId ?? string.Empty,
            dailyFigureDate ?? string.Empty,
            amount ?? string.Empty,
            tranCode ?? string.Empty,
            tranType ?? string.Empty,
            description ?? string.Empty
        );

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToUpperInvariant();
    }

    // Parse 21G's `dailyFigureDate_YYYYMMDD` string into a DateOnly.
    // The WSDL types it as a string in `yyyyMMdd` format (no separator,
    // no time, no timezone).
    public static DateOnly? ParseDailyFigureDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateOnly.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed
        )
            ? parsed
            : null;
    }

    // 21G sends `amount` as a string — usually a decimal like "10.50".
    // Convert to signed cents (long). Returns null on unparseable input
    // so the caller can reject the request.
    public static long? ParseAmountToCents(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec)
        )
        {
            return null;
        }
        return (long)Math.Round(dec * 100m, MidpointRounding.AwayFromZero);
    }
}

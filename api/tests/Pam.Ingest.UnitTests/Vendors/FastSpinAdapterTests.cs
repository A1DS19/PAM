using System.Text;
using FluentAssertions;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Contracts.Vendors;
using Pam.Ingest.Vendors.FastSpin;
using Xunit;

namespace Pam.Ingest.UnitTests.Vendors;

// Adapter-level shape tests. The endpoint-level forward + persist flow
// has its own integration test against a fake IFastSpinUpstream in
// Pam.IntegrationTests.
public sealed class FastSpinAdapterTests
{
    [Fact]
    public void Place_bet_transfer_with_clean_response_builds_full_command()
    {
        // type=1 → bet → AmountCents negative regardless of the positive
        // amount on the wire. balance from response → VendorBalanceAfterCents
        // in cents. merchantTxId → DownstreamReference. Outcome → Forwarded.
        var requestJson =
            """
            {
              "transferId": "abc-123",
              "acctId": "PLAYER1",
              "currency": "EUR",
              "amount": 2.50,
              "type": 1,
              "gameCode": "S-RM01",
              "referenceId": "round-99",
              "transferTime": "20260513T142233"
            }
            """;
        var responseJson =
            """
            { "transferId":"abc-123", "merchantTxId":"DOC-555", "acctId":"PLAYER1",
              "balance": 1050.15, "code": 0, "msg": "Success", "serialNo":"sn1", "merchantCode":"FS" }
            """;

        var cmd = FastSpinAdapter.ExtractCommand(
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes(responseJson),
            DownstreamStatus.Forwarded,
            latencyMs: 87
        );

        cmd.Should().NotBeNull();
        cmd!.VendorId.Should().Be(VendorCodes.FastSpin);
        cmd.VendorReference.Should().Be("abc-123");
        cmd.AmountCents.Should().Be(-250, "type=1 is a bet → negative cents");
        cmd.Currency.Should().Be("EUR");
        cmd.Kind.Should().Be(TransactionKind.Risk);
        cmd.RoundId.Should().Be("round-99");
        cmd.Description.Should().Be("S-RM01");
        cmd.VendorBalanceAfterCents.Should().Be(105015);
        cmd.DownstreamReference.Should().Be("DOC-555");
        cmd.DownstreamOutcomeCode.Should().Be(0);
        cmd.DownstreamOutcomeMessage.Should().Be("Success");
        cmd.DownstreamStatus.Should().Be(DownstreamStatus.Forwarded);
        cmd.DownstreamLatencyMs.Should().Be(87);
    }

    [Fact]
    public void Payout_transfer_uses_positive_cents()
    {
        // type=4 → win → AmountCents positive.
        var requestJson =
            """
            { "transferId":"x", "acctId":"P", "currency":"USD", "amount":10, "type":4 }
            """;
        var responseJson = """ { "balance": 100.0, "code": 0 } """;

        var cmd = FastSpinAdapter.ExtractCommand(
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes(responseJson),
            DownstreamStatus.Forwarded,
            latencyMs: 10
        );

        cmd.Should().NotBeNull();
        cmd!.Kind.Should().Be(TransactionKind.Win);
        cmd.AmountCents.Should().Be(1000);
    }

    [Fact]
    public void Bonus_transfer_maps_to_Bonus_kind()
    {
        var requestJson =
            """
            { "transferId":"x", "acctId":"P", "currency":"USD", "amount":5, "type":7 }
            """;
        var responseJson = """ { "balance": 50.0, "code": 0 } """;

        var cmd = FastSpinAdapter.ExtractCommand(
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes(responseJson),
            DownstreamStatus.Forwarded,
            latencyMs: 5
        );

        cmd!.Kind.Should().Be(TransactionKind.Bonus);
        cmd.AmountCents.Should().Be(500);
    }

    [Fact]
    public void Unknown_transfer_type_returns_null()
    {
        // type=99 is not modeled. Endpoint relays GBS's response anyway
        // but doesn't persist a row — null tells it not to.
        var requestJson =
            """
            { "transferId":"x", "acctId":"P", "currency":"USD", "amount":1, "type":99 }
            """;
        var responseJson = """ { "code": 0 } """;

        var cmd = FastSpinAdapter.ExtractCommand(
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes(responseJson),
            DownstreamStatus.Forwarded,
            latencyMs: 5
        );

        cmd.Should().BeNull();
    }

    [Fact]
    public void Missing_transferId_returns_null()
    {
        var requestJson =
            """
            { "acctId":"P", "currency":"USD", "amount":1, "type":1 }
            """;
        var responseJson = """ { "code": 0 } """;

        var cmd = FastSpinAdapter.ExtractCommand(
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes(responseJson),
            DownstreamStatus.Forwarded,
            latencyMs: 5
        );

        cmd.Should().BeNull();
    }

    [Fact]
    public void Upstream_timeout_still_records_request_side()
    {
        // No response body, but the request alone is enough to persist
        // a row with downstream_status=UpstreamTimeout. Vendor will
        // retry with the same transferId; idempotency handles the dup.
        var requestJson =
            """
            { "transferId":"to-1", "acctId":"P", "currency":"USD", "amount":3, "type":1 }
            """;

        var cmd = FastSpinAdapter.ExtractCommand(
            Encoding.UTF8.GetBytes(requestJson),
            ReadOnlySpan<byte>.Empty,
            DownstreamStatus.UpstreamTimeout,
            latencyMs: 30000
        );

        cmd.Should().NotBeNull();
        cmd!.VendorBalanceAfterCents.Should().BeNull();
        cmd.DownstreamReference.Should().BeNull();
        cmd.DownstreamOutcomeCode.Should().BeNull();
        cmd.DownstreamStatus.Should().Be(DownstreamStatus.UpstreamTimeout);
        cmd.DownstreamLatencyMs.Should().Be(30000);
    }

    [Fact]
    public void Malformed_request_json_returns_null()
    {
        var cmd = FastSpinAdapter.ExtractCommand(
            Encoding.UTF8.GetBytes("{ not valid json"),
            Encoding.UTF8.GetBytes("""{"code":0}"""),
            DownstreamStatus.Forwarded,
            latencyMs: 5
        );

        cmd.Should().BeNull();
    }

    [Fact]
    public void Malformed_response_json_still_persists_request_side()
    {
        var requestJson =
            """
            { "transferId":"x", "acctId":"P", "currency":"USD", "amount":3, "type":1 }
            """;

        var cmd = FastSpinAdapter.ExtractCommand(
            Encoding.UTF8.GetBytes(requestJson),
            Encoding.UTF8.GetBytes("{ broken"),
            DownstreamStatus.Forwarded,
            latencyMs: 12
        );

        cmd.Should().NotBeNull();
        cmd!.VendorBalanceAfterCents.Should().BeNull();
        cmd.DownstreamOutcomeCode.Should().BeNull();
        cmd.DownstreamStatus.Should().Be(DownstreamStatus.Forwarded);
        cmd.DownstreamLatencyMs.Should().Be(12);
    }
}

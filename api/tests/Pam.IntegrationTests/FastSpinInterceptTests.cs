using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Contracts.Vendors;
using Pam.Ingest.Data;
using Pam.Ingest.Vendors.FastSpin;
using Xunit;

namespace Pam.IntegrationTests;

// End-to-end coverage for the FastSpin Phase-A intercept endpoint.
// PamApiFactory's default `Ingest:Vendors:FastSpin:UpstreamUrl` is a
// dummy http://test-fastspin-upstream.local — these tests REPLACE the
// real IFastSpinUpstream with a fake that returns canned responses,
// so no actual outbound HTTP is attempted.
[Collection(nameof(PamContainersCollection))]
public sealed class FastSpinInterceptTests(PamContainersFixture containers)
{
    [Fact]
    public async Task Transfer_persists_row_with_combined_request_and_response_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        var transferId = $"fs-tx-{Guid.NewGuid():N}";

        var fakeResponseBody = """
            {"transferId":"%TID%","merchantTxId":"DOC-7777","acctId":"PLAYER1",
             "balance":1050.15,"code":0,"msg":"Success","serialNo":"sn",
             "merchantCode":"FS"}
            """.Replace("%TID%", transferId, StringComparison.Ordinal);

        var fakeUpstream = new FakeFastSpinUpstream(
            new FastSpinUpstreamResult(
                StatusCode: 200,
                Body: Encoding.UTF8.GetBytes(fakeResponseBody),
                ContentType: "application/json",
                LatencyMs: 73,
                Outcome: DownstreamStatus.Forwarded
            )
        );

        await using var inner = new PamApiFactory(containers);
        await using var factory = inner.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFastSpinUpstream>();
                services.AddSingleton<IFastSpinUpstream>(fakeUpstream);
            })
        );
        var client = factory.CreateClient();

        var requestBody = $$"""
            {"transferId":"{{transferId}}","acctId":"PLAYER1","currency":"EUR",
             "amount":2.50,"type":1,"gameCode":"S-RM01","referenceId":"r1",
             "transferTime":"20260513T142233"}
            """;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/ingest/vendors/{VendorCodes.FastSpin}/main"
        )
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("API", "transfer");
        request.Headers.Add("Digest", "any-string-the-real-gbs-would-validate");

        var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var relayed = await response.Content.ReadAsStringAsync(ct);
        relayed.Should().Be(fakeResponseBody, "PAM relays GBS's bytes verbatim");

        // Row should be in ingest.vendor_transactions with the combined fields.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        var row = await db
            .VendorTransactions.AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.VendorId == VendorCodes.FastSpin && t.VendorReference == transferId,
                ct
            );

        row.Should().NotBeNull();
        row!.AmountCents.Should().Be(-250, "type=1 (bet) → negative cents");
        row.Currency.Should().Be("EUR");
        row.Kind.Should().Be(TransactionKind.Risk);
        row.VendorBalanceAfterCents.Should().Be(105015, "GBS balance 1050.15 → 105015 cents");
        row.DownstreamReference.Should().Be("DOC-7777");
        row.DownstreamOutcomeCode.Should().Be(0);
        row.DownstreamOutcomeMessage.Should().Be("Success");
        row.DownstreamStatus.Should().Be(DownstreamStatus.Forwarded);
        row.DownstreamLatencyMs.Should().Be(73);
    }

    [Fact]
    public async Task GetBalance_forwards_and_relays_without_persisting()
    {
        var ct = TestContext.Current.CancellationToken;

        var fakeUpstream = new FakeFastSpinUpstream(
            new FastSpinUpstreamResult(
                StatusCode: 200,
                Body: Encoding.UTF8.GetBytes(
                    """{"acctInfo":{"acctId":"PLAYER1","userName":"P1","currency":"EUR","balance":1050.15},"code":0,"msg":"success","merchantCode":"FS","serialNo":"sn"}"""
                ),
                ContentType: "application/json",
                LatencyMs: 15,
                Outcome: DownstreamStatus.Forwarded
            )
        );

        await using var inner = new PamApiFactory(containers);
        await using var factory = inner.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFastSpinUpstream>();
                services.AddSingleton<IFastSpinUpstream>(fakeUpstream);
            })
        );
        var client = factory.CreateClient();

        var preCount = await CountFastSpinRowsAsync(factory, ct);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/ingest/vendors/{VendorCodes.FastSpin}/main"
        )
        {
            Content = new StringContent(
                """{"acctId":"PLAYER1","gameCode":"S-RM01","merchantCode":"FS","serialNo":"sn"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
        request.Headers.Add("API", "getBalance");

        var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var postCount = await CountFastSpinRowsAsync(factory, ct);
        postCount.Should().Be(preCount, "getBalance is a query — no row persists");
    }

    [Fact]
    public async Task Upstream_timeout_records_row_with_UpstreamTimeout_status_and_returns_503()
    {
        var ct = TestContext.Current.CancellationToken;
        var transferId = $"fs-to-{Guid.NewGuid():N}";

        var fakeUpstream = new FakeFastSpinUpstream(
            new FastSpinUpstreamResult(
                StatusCode: StatusCodes.Status503ServiceUnavailable,
                Body: ReadOnlyMemory<byte>.Empty,
                ContentType: "application/json",
                LatencyMs: 30000,
                Outcome: DownstreamStatus.UpstreamTimeout
            )
        );

        await using var inner = new PamApiFactory(containers);
        await using var factory = inner.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFastSpinUpstream>();
                services.AddSingleton<IFastSpinUpstream>(fakeUpstream);
            })
        );
        var client = factory.CreateClient();

        var requestBody = $$"""
            {"transferId":"{{transferId}}","acctId":"PLAYER1","currency":"EUR",
             "amount":1,"type":1}
            """;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/ingest/vendors/{VendorCodes.FastSpin}/main"
        )
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("API", "transfer");

        var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        var row = await db
            .VendorTransactions.AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.VendorId == VendorCodes.FastSpin && t.VendorReference == transferId,
                ct
            );

        row.Should().NotBeNull("PAM still records what it received even when GBS timed out");
        row!.DownstreamStatus.Should().Be(DownstreamStatus.UpstreamTimeout);
        row.DownstreamReference.Should().BeNull();
        row.VendorBalanceAfterCents.Should().BeNull();
        row.DownstreamLatencyMs.Should().Be(30000);
    }

    [Fact]
    public async Task Upstream_unreachable_returns_502_and_does_not_persist()
    {
        // UpstreamUnreachable means the forward never landed. We deliberately
        // skip persistence — without ANY response, persisting would imply
        // "we saw this and it landed somewhere", which would be a lie.
        // Vendor retries; on the retry that succeeds, the row commits.
        var ct = TestContext.Current.CancellationToken;
        var transferId = $"fs-ur-{Guid.NewGuid():N}";

        var fakeUpstream = new FakeFastSpinUpstream(
            new FastSpinUpstreamResult(
                StatusCode: StatusCodes.Status502BadGateway,
                Body: ReadOnlyMemory<byte>.Empty,
                ContentType: "application/json",
                LatencyMs: 5,
                Outcome: DownstreamStatus.UpstreamUnreachable
            )
        );

        await using var inner = new PamApiFactory(containers);
        await using var factory = inner.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFastSpinUpstream>();
                services.AddSingleton<IFastSpinUpstream>(fakeUpstream);
            })
        );
        var client = factory.CreateClient();

        var requestBody = $$"""
            {"transferId":"{{transferId}}","acctId":"PLAYER1","currency":"EUR",
             "amount":1,"type":1}
            """;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/ingest/vendors/{VendorCodes.FastSpin}/main"
        )
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("API", "transfer");

        var response = await client.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        var row = await db
            .VendorTransactions.AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.VendorId == VendorCodes.FastSpin && t.VendorReference == transferId,
                ct
            );

        row.Should().BeNull("UpstreamUnreachable means the forward never landed; vendor retries");
    }

    private static async Task<int> CountFastSpinRowsAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken ct
    )
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        return await db
            .VendorTransactions.AsNoTracking()
            .CountAsync(t => t.VendorId == VendorCodes.FastSpin, ct);
    }
}

// Returns one canned response per call. Sufficient for the current
// scenarios — extend with a queue if a test needs multiple distinct
// responses in sequence.
internal sealed class FakeFastSpinUpstream(FastSpinUpstreamResult canned) : IFastSpinUpstream
{
    public Task<FastSpinUpstreamResult> ForwardAsync(
        HttpRequest inboundRequest,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    ) => Task.FromResult(canned);
}

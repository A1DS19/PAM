using System.Text;
using Microsoft.AspNetCore.Http;
using Pam.Ingest.Contracts.Transactions.Models;

namespace Pam.Ingest.Vendors.FastSpin.Stress;

// Stress-test-only fake of IFastSpinUpstream. Registered ONLY when
// Stress:FastSpinUpstreamStub:Enabled is true (see IngestModule). Returns
// a canned 200 response with a FastSpinTransferResponse-shaped body so
// FastSpinAdapter happily parses the downstream_* columns. No outbound
// HTTP — the stress run measures PAM's hot path (intercept → adapter →
// IngestTransactionHandler → outbox), not GBS's network.
//
// Do not promote to production. The real FastSpinUpstream stays the only
// thing that ever forwards bytes to GBS.
public sealed class StubFastSpinUpstream : IFastSpinUpstream
{
    private static readonly byte[] CannedBody = Encoding.UTF8.GetBytes(
        """{"transferId":"stub","merchantTxId":"STUB-TX","acctId":"PLAYER","balance":1000.00,"code":0,"msg":"stub","serialNo":"sn","merchantCode":"FS"}"""
    );

    public Task<FastSpinUpstreamResult> ForwardAsync(
        HttpRequest inboundRequest,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new FastSpinUpstreamResult(
                StatusCode: StatusCodes.Status200OK,
                Body: CannedBody,
                ContentType: "application/json",
                LatencyMs: 0,
                Outcome: DownstreamStatus.Forwarded
            )
        );
}

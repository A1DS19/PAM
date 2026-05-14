using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pam.Ingest.Contracts.Transactions.Models;

namespace Pam.Ingest.Vendors.FastSpin;

// Result of one forward attempt. The endpoint relays Body + ContentType +
// StatusCode back to FastSpin verbatim; Outcome + LatencyMs feed the
// canonical row's downstream_* columns. Body is a ReadOnlyMemory<byte>
// so it satisfies CA1819 (no array property) AND avoids a re-copy when
// the endpoint writes it to the response stream.
public sealed record FastSpinUpstreamResult(
    int StatusCode,
    ReadOnlyMemory<byte> Body,
    string ContentType,
    int LatencyMs,
    DownstreamStatus Outcome
);

public interface IFastSpinUpstream
{
    // Forwards a FastSpin call to GBS unchanged. Buffer-copy semantics:
    // the body bytes are written to the upstream request as-is so GBS's
    // own Digest validation (MD5(body + securityKey)) succeeds — any
    // re-encode would break the signature.
    Task<FastSpinUpstreamResult> ForwardAsync(
        HttpRequest inboundRequest,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    );
}

public sealed class FastSpinUpstream(HttpClient http, ILogger<FastSpinUpstream> logger)
    : IFastSpinUpstream
{
    // FastSpin uses HTTP headers (API, Digest, etc.) instead of body
    // fields for verb dispatch + signing. We must forward those, but
    // NOT the framework-set ones (Host, Content-Length — HttpClient
    // sets those itself based on the destination) and NOT Content-Type
    // (we set it explicitly below; letting the copy loop write to
    // Content.Headers.ContentType replaces our value with one that
    // GBS can't read on some inbound shapes, causing a 415 upstream).
    private static readonly HashSet<string> ExcludedHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Host",
            "Content-Length",
            "Content-Type",
            "Connection",
            "Keep-Alive",
            "Transfer-Encoding",
            "Upgrade",
            "Proxy-Authorization",
            "Proxy-Authenticate",
            "TE",
            "Trailer",
        };

    public async Task<FastSpinUpstreamResult> ForwardAsync(
        HttpRequest inboundRequest,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var upstreamReq = new HttpRequestMessage(HttpMethod.Post, http.BaseAddress);
            upstreamReq.Content = new ReadOnlyMemoryContent(body);

            // Force Content-Type via explicit header add (not the typed
            // .ContentType property — empirically on .NET 10 +
            // ReadOnlyMemoryContent the property setter silently no-ops
            // for some inbound shapes, leaving HttpClient to send no
            // Content-Type at all and IIS to infer application/octet-stream).
            // Remove first to guarantee a clean single-value header.
            var inboundContentType = inboundRequest.ContentType ?? "application/json";
            upstreamReq.Content.Headers.Remove("Content-Type");
            upstreamReq.Content.Headers.TryAddWithoutValidation("Content-Type", inboundContentType);
            logger.LogDebug(
                "FastSpin forward: Content-Type={ContentType}, body={Bytes} bytes",
                inboundContentType,
                body.Length
            );

            foreach (var (name, value) in inboundRequest.Headers)
            {
                if (ExcludedHeaders.Contains(name))
                {
                    continue;
                }
                // Try as a request header first; fall through to content
                // header if HttpClient routes it that way.
                if (!upstreamReq.Headers.TryAddWithoutValidation(name, value.ToArray()))
                {
                    upstreamReq.Content.Headers.TryAddWithoutValidation(name, value.ToArray());
                }
            }

            using var upstreamResp = await http.SendAsync(
                upstreamReq,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken
            );

            var respBody = await upstreamResp.Content.ReadAsByteArrayAsync(cancellationToken);
            var respContentType =
                upstreamResp.Content.Headers.ContentType?.ToString() ?? "application/json";
            var status = (int)upstreamResp.StatusCode;

            stopwatch.Stop();
            var elapsed = (int)stopwatch.ElapsedMilliseconds;

            // 2xx → Forwarded (the body IS the response, even when it
            // carries a vendor-level rejection via code != 0).
            // 4xx/5xx → UpstreamError. 4xx means our request was malformed
            // from GBS's point of view (415, 400, 404 with a non-FastSpin
            // body) — the body is HTTP infrastructure error text, not a
            // FastSpin response, and parsing it as one would lie.
            var outcome = status is >= 200 and < 300
                ? DownstreamStatus.Forwarded
                : DownstreamStatus.UpstreamError;

            return new FastSpinUpstreamResult(status, respBody, respContentType, elapsed, outcome);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout fired (request CT was NOT triggered).
            stopwatch.Stop();
            logger.LogWarning(
                ex,
                "FastSpin upstream timed out after {ElapsedMs}ms",
                stopwatch.ElapsedMilliseconds
            );
            return new FastSpinUpstreamResult(
                StatusCode: StatusCodes.Status503ServiceUnavailable,
                Body: ReadOnlyMemory<byte>.Empty,
                ContentType: "application/json",
                LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                Outcome: DownstreamStatus.UpstreamTimeout
            );
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogWarning(
                ex,
                "FastSpin upstream unreachable after {ElapsedMs}ms",
                stopwatch.ElapsedMilliseconds
            );
            return new FastSpinUpstreamResult(
                StatusCode: StatusCodes.Status502BadGateway,
                Body: ReadOnlyMemory<byte>.Empty,
                ContentType: "application/json",
                LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                Outcome: DownstreamStatus.UpstreamUnreachable
            );
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Pam.IntegrationTests;

// CreateBrand requires Permissions.operators.brands.write since Phase 3.
// These tests verify the auth fence is intact end-to-end — anonymous
// requests are rejected before any handler runs, and the route is at
// least mapped (404 would indicate a routing regression).
//
// A future PR adds a "test-only" auth seam (a partial Program override
// that swaps in a development bearer scheme) so the create/get round
// trip can run authenticated. Skipped here on purpose: we want the gate
// PR to verify the harness boots and the bus + DB + Redis all wire up,
// not to backfill auth-aware integration coverage.
[Collection(nameof(PamContainersCollection))]
public sealed class OperatorsBrandTests(PamContainersFixture containers)
{
    [Fact]
    public async Task CreateBrand_Requires_Authentication()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/v1/operators/brands", UriKind.Relative),
            new
            {
                name = "BetAnything EU",
                slug = "betanything-eu",
                jurisdiction = "EU",
            },
            ct
        );

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.Unauthorized,
                "the fallback authorization policy rejects unauthenticated requests"
            );
    }
}

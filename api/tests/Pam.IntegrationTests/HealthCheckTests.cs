using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Pam.IntegrationTests;

[Collection(nameof(PamContainersCollection))]
public sealed class HealthCheckTests(PamContainersFixture containers)
{
    [Fact]
    public async Task LiveProbe_Returns_Healthy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyProbe_Returns_Healthy_When_Dependencies_Up()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        // Force the host to start so hosted services + EF migrations have
        // run before we ask about readiness.
        _ = factory.Services;

        // Resolve the HealthCheckService directly so a failed check tells
        // us *which* check failed instead of the bare "Unhealthy" line the
        // default response writer emits.
        var hcs = factory.Services.GetRequiredService<HealthCheckService>();
        var report = await hcs.CheckHealthAsync(
            r => r.Tags.Contains("ready", StringComparer.Ordinal),
            ct
        );

        var failing = report
            .Entries.Where(e => e.Value.Status != HealthStatus.Healthy)
            .Select(e =>
                $"{e.Key}={e.Value.Status} ({e.Value.Exception?.GetType().Name}: {e.Value.Exception?.Message})"
            );

        report
            .Status.Should()
            .Be(
                HealthStatus.Healthy,
                "ready checks should pass against testcontainers. Failing: {0}",
                string.Join("; ", failing)
            );
    }
}

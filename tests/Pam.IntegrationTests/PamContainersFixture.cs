using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace Pam.IntegrationTests;

// Boots Postgres 17 + RabbitMQ 4 + Redis 7 in throwaway containers for
// the duration of the test session. Used as an ICollectionFixture so the
// startup cost (~10–20s) is paid once across every integration test.
//
// Each container picks its own ephemeral host port — multiple test runs
// don't clash with each other or with the dev-loop containers from
// docker-compose.yml.
public sealed class PamContainersFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pam")
            .WithUsername("pam")
            .WithPassword("pam_test_password")
            .Build();

    public RabbitMqContainer Rabbit { get; } =
        new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Rabbit.StartAsync(), Redis.StartAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(
            Postgres.DisposeAsync().AsTask(),
            Rabbit.DisposeAsync().AsTask(),
            Redis.DisposeAsync().AsTask()
        );
    }
}

// Shared xUnit collection so the container fixture is reused across every
// integration test class instead of paying the boot cost per file.
[CollectionDefinition(nameof(PamContainersCollection))]
public sealed class PamContainersCollection : ICollectionFixture<PamContainersFixture> { }

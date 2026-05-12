using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace Pam.IntegrationTests;

// Boots SQL Server 2022 + RabbitMQ 4 + Redis 7 in throwaway containers
// for the duration of the test session. Used as an ICollectionFixture so
// the startup cost is paid once across every integration test.
//
// SQL Server boot on Apple Silicon is slow (~30–60s under Rosetta) — CI
// runs on amd64 where it's much faster. Each container picks its own
// ephemeral host port; multiple test runs don't clash with each other
// or with the dev-loop containers from docker-compose.yml.
public sealed class PamContainersFixture : IAsyncLifetime
{
    public MsSqlContainer Sql { get; } =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            // Must meet SA password complexity (>=8 chars, mixed case, digit, special).
            .WithPassword("Pam_test_password_123!")
            .Build();

    public RabbitMqContainer Rabbit { get; } =
        new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    public RedisContainer Redis { get; } = new RedisBuilder("redis:7-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Sql.StartAsync(), Rabbit.StartAsync(), Redis.StartAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(
            Sql.DisposeAsync().AsTask(),
            Rabbit.DisposeAsync().AsTask(),
            Redis.DisposeAsync().AsTask()
        );
    }
}

// Shared xUnit collection so the container fixture is reused across every
// integration test class instead of paying the boot cost per file.
[CollectionDefinition(nameof(PamContainersCollection))]
public sealed class PamContainersCollection : ICollectionFixture<PamContainersFixture> { }

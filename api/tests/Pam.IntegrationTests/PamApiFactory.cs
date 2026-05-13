using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Pam.IntegrationTests;

// WebApplicationFactory<Program> overrides the host configuration so the
// API points at the throwaway containers from PamContainersFixture
// instead of the dev-loop services in docker-compose.yml.
//
// Program.cs reads ConnectionStrings + MessageBroker via builder.Configuration
// BEFORE builder.Build() (the Redis multiplexer is awaited at the top
// level), so the overrides must be injected via ConfigureAppConfiguration
// — services-collection overrides happen too late.
public sealed class PamApiFactory(PamContainersFixture containers)
    : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Testcontainers' Rabbit image rolls fresh credentials per
        // container — `guest/guest` is wrong. The connection string
        // (amqp://user:pass@host:port) is the source of truth.
        var rabbitUri = new Uri(containers.Rabbit.GetConnectionString());
        var userInfo = rabbitUri.UserInfo.Split(':', 2);
        var rabbitUser = Uri.UnescapeDataString(userInfo[0]);
        var rabbitPassword = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";

        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(
            (_, config) =>
            {
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        // Testcontainers.MsSql emits a connection string without
                        // TrustServerCertificate/Encrypt — the SA cert isn't
                        // signed by a CA the .NET client trusts, so we append
                        // them to dodge a TLS handshake failure on every test.
                        ["ConnectionStrings:Pam"] =
                            containers.Sql.GetConnectionString()
                            + ";TrustServerCertificate=True;Encrypt=False",
                        ["ConnectionStrings:Redis"] = containers.Redis.GetConnectionString(),
                        ["MessageBroker:Host"] = rabbitUri.Host,
                        ["MessageBroker:Port"] = rabbitUri.Port.ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        ),
                        ["MessageBroker:VirtualHost"] = "/",
                        ["MessageBroker:Username"] = rabbitUser,
                        ["MessageBroker:Password"] = rabbitPassword,
                        // Distinct DP application name so test runs don't
                        // share keys across CI parallel invocations.
                        ["DataProtection:ApplicationName"] =
                            $"pam-api/IntegrationTests/{Guid.NewGuid():N}",
                        // Disable the reconciliation BackgroundService by
                        // default — tests that exercise the reconciler
                        // invoke IOutboxReconciler.ScanAndRepublishAsync
                        // explicitly so the assertion isn't racing the
                        // timer.
                        ["Messaging:Reconciliation:Enabled"] = "false",
                    }
                );
            }
        );
        return base.CreateHost(builder);
    }
}

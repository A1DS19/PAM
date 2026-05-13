using System.Threading.RateLimiting;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pam.Audit;
using Pam.Identity;
using Pam.Identity.Contracts.Permissions;
using Pam.Identity.Data;
using Pam.Ingest;
using Pam.Notifications;
using Pam.Operators;
using Pam.Players;
using Pam.Shared.Exceptions.Handlers;
using Pam.Shared.Extensions;
using Pam.Shared.Http;
using Pam.Shared.Messaging.Extensions;
using Pam.Shared.Messaging.Reconciliation;
using Pam.Wallet;
using RedisRateLimiting;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configuration precedence (highest first): environment variables,
// appsettings.{env}.json, appsettings.json. ASP.NET wires this by default —
// no extra code needed. Production secrets arrive as env vars from the
// orchestrator (systemd unit, k3s Secret, Swarm secret).

builder.Host.UseSerilog(
    (ctx, _, cfg) =>
        cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", "pam-api")
            .Enrich.WithProperty("env", ctx.HostingEnvironment.EnvironmentName)
);

var moduleAssemblies = new[]
{
    typeof(IdentityModule).Assembly,
    typeof(IngestModule).Assembly,
    typeof(NotificationsModule).Assembly,
    typeof(OperatorsModule).Assembly,
    typeof(PlayersModule).Assembly,
    typeof(WalletModule).Assembly,
};

builder.Services.AddPamShared();
builder.Services.AddPamMediatR(moduleAssemblies);

// Consumer assemblies for MassTransit auto-discovery — Pam.Notifications
// houses the integration-event subscribers (welcome emails, transactional
// receipts, ...). Pam.Identity/Pam.Operators don't consume events yet.
//
// Outbox lives on PamMessagingDbContext (schema "messaging"), owned by
// Pam.Shared.Messaging. Single .UseBusOutbox() call inside this method —
// no per-module ConfigureOutbox hooks. See docs/DECISIONS.md ADR on the
// outbox topology.
//
// Stress:DiscardConsumers:Enabled gates the no-op stress consumers in
// Pam.Notifications.Stress. Off everywhere except ASPNETCORE_ENVIRONMENT=
// Stress, so production never binds a queue to a discard sink.
var stressDiscardConsumersEnabled = builder.Configuration.GetValue(
    "Stress:DiscardConsumers:Enabled",
    false
);
Func<Type, bool>? consumerFilter = stressDiscardConsumersEnabled
    ? null
    : t =>
        !string.Equals(t.Namespace, "Pam.Notifications.Stress", StringComparison.Ordinal)
        && t.Namespace?.StartsWith("Pam.Notifications.Stress.", StringComparison.Ordinal) != true;
builder.Services.AddPamMassTransit(
    builder.Configuration,
    consumerFilter,
    typeof(NotificationsModule).Assembly
);

// Bind OutboxReconciliationOptions from Messaging:Reconciliation. The
// host has the configuration binder available; Pam.Shared.Messaging
// doesn't take the AspNetCore framework dep just for this.
builder.Services.Configure<OutboxReconciliationOptions>(
    builder.Configuration.GetSection(OutboxReconciliationOptions.SectionName)
);

builder.Services.AddHttpContextAccessor();

// Notifications first — Identity's forgot-password / send-confirmation-email
// handlers resolve IEmailSender from Notifications during DI graph building.
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);
builder.Services.AddOperatorsModule(builder.Configuration);
builder.Services.AddPlayersModule(builder.Configuration);
builder.Services.AddWalletModule(builder.Configuration);
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddIngestModule(builder.Configuration);

// Data Protection master keyring → IdentityDbContext.DataProtectionKeys.
// Without this, each replica generates its own keyring under the local
// user profile; cookies + OpenIddict state strings issued by replica A
// fail to validate on replica B, and rolling deploys invalidate every
// session. ApplicationName isolates keys across environments sharing a
// database (don't share keys between staging and prod).
//
// IdentityModule registers IdentityDbContext above; this hook resolves
// the same registration. AddIdentityModule must run before this line.
var dataProtectionApplicationName =
    builder.Configuration["DataProtection:ApplicationName"]
    ?? $"pam-api/{builder.Environment.EnvironmentName}";
builder
    .Services.AddDataProtection()
    .SetApplicationName(dataProtectionApplicationName)
    .PersistKeysToDbContext<IdentityDbContext>();

// Vendors and back-office clients alike speak enum names on the wire,
// not ordinals. The response side already stringifies (e.g.
// `result.Status.ToString()` in TwentyOneGAdapter.FormatResponseAsync),
// so the request side must accept strings too — otherwise a payload
// like `"kind": "Risk"` (what a real vendor sends) deserializes to a
// JsonException before the handler runs. JsonStringEnumConverter
// handles both directions. Affects HttpRequest.ReadFromJsonAsync and
// minimal-API body binding.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()
    );
});

builder.Services.AddCarter(new DependencyContextAssemblyCatalog(moduleAssemblies));

// MVC controllers are required by OpenIddict's AuthorizationController +
// UserinfoController in Pam.Identity. Carter (vertical slices) and MVC
// (these two controllers only) coexist via app.MapCarter() + app.MapControllers().
builder.Services.AddControllers();

builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<CustomExceptionHandler>();

// Authorization: fallback policy requires bearer auth via the OpenIddict
// validation scheme. Endpoints opt out via .AllowAnonymous(). One named
// policy per permission code so [Authorize(Policy = "Permissions.<code>")]
// is the granular check; platform.admin is a meta-permission that grants
// every other policy via the OR assertion.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(
        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
    )
        .RequireAuthenticatedUser()
        .Build();

    foreach (var code in PermissionCodes.All)
    {
        options.AddPolicy(
            $"Permissions.{code}",
            policy =>
                policy
                    .AddAuthenticationSchemes(
                        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                    )
                    .RequireAssertion(ctx =>
                        ctx.User.HasClaim(PamClaimTypes.Permission, code)
                        || ctx.User.HasClaim(
                            PamClaimTypes.Permission,
                            PermissionCodes.Platform.Admin
                        )
                    )
        );
    }
});

// CORS for the back-office SPA's dev origin. Production overrides via
// `Cors:AllowedOrigins` in appsettings or an env var.
var corsOrigins =
    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    )
);

// By default, Minimal APIs handle parameter-binding failures internally and
// short-circuit to IProblemDetailsService — bypassing our IExceptionHandler.
// Routing them through our handler keeps a single place that formats errors
// AND avoids the framework's dev-only ProblemDetails extension that leaks
// request headers + cookies in the response body.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o =>
    o.ThrowOnBadRequest = true
);

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        // Defense-in-depth: any ProblemDetails the framework writes directly
        // (404 from a missing route, 405, 415, etc.) gets the dev "exception"
        // extension stripped so we never leak headers/cookies/stack traces.
        ctx.ProblemDetails.Extensions.Remove("exception");
        if (!ctx.ProblemDetails.Extensions.ContainsKey("traceId"))
        {
            ctx.ProblemDetails.Extensions["traceId"] =
                System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
        }
    };
});

// Trust proxy headers so RemoteIpAddress / scheme reflect the real client
// when behind a load balancer or ingress. KnownProxies/Networks must be set
// in production via configuration; cleared here so dev runs see the actual
// loopback IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Single shared multiplexer — StackExchange.Redis is thread-safe and
// reconnects internally. AbortConnect=false lets the API boot even when
// Redis is briefly unreachable; the multiplexer retries on each command.
// Rate-limited endpoints will still surface a 5xx if Redis stays down,
// which is intentional — the alternative is "no rate limit on /login",
// which is worse than a brief outage.
var redisConnectionString =
    builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured");
var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
redisOptions.AbortOnConnectFail = false;
redisOptions.ClientName = "pam-api";
var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddPamCaching();

// Rate limiting can be globally disabled via RateLimiting:Enabled=false.
// Stress runs flip this so we measure the system, not our own limiter.
// Policies are still registered (the .RequireRateLimiting chain on
// endpoints must resolve to a valid policy) — they just become no-op
// partitions when the flag is off.
var rateLimitingEnabled = builder.Configuration.GetValue("RateLimiting:Enabled", true);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static string ClientPartitionKey(HttpContext ctx)
    {
        // Prefer authenticated identity; fall back to forwarded client IP.
        // UseForwardedHeaders runs before this, so RemoteIpAddress reflects
        // X-Forwarded-For when configured.
        var sub = ctx.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(sub))
        {
            return $"sub:{sub}";
        }

        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "anon";
        }
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }
        return $"ip:{ip}";
    }

    // Redis-backed partitions — replicas share the count via a sorted set
    // (sliding) or atomic INCR+EXPIRE (fixed window). Same policy shape as
    // the previous in-memory limiters, but enforcement is global.
    //
    // Note: RedisSlidingWindowRateLimiter doesn't take SegmentsPerWindow —
    // it implements the textbook sliding window via a sorted set keyed by
    // request timestamp, which is more accurate than segmented buckets.
    o.AddPolicy(
        "auth-sensitive",
        ctx =>
            rateLimitingEnabled
                ? RedisRateLimitPartition.GetFixedWindowRateLimiter(
                    ClientPartitionKey(ctx),
                    _ => new RedisFixedWindowRateLimiterOptions
                    {
                        ConnectionMultiplexerFactory = () => redis,
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = 5,
                    }
                )
                : RateLimitPartition.GetNoLimiter<string>("disabled")
    );

    o.AddPolicy(
        "api-default",
        ctx =>
            rateLimitingEnabled
                ? RedisRateLimitPartition.GetSlidingWindowRateLimiter(
                    ClientPartitionKey(ctx),
                    _ => new RedisSlidingWindowRateLimiterOptions
                    {
                        ConnectionMultiplexerFactory = () => redis,
                        Window = TimeSpan.FromSeconds(30),
                        PermitLimit = 100,
                    }
                )
                : RateLimitPartition.GetNoLimiter<string>("disabled")
    );

    o.OnRejected = async (context, cancellationToken) =>
    {
        context
            .HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiter")
            .LogWarning(
                "Rate limit exceeded: Path={Path} Method={Method} RemoteIp={RemoteIp}",
                context.HttpContext.Request.Path,
                context.HttpContext.Request.Method,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            );

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = (
                (int)retryAfter.TotalSeconds
            ).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { title = "Too Many Requests", status = 429 },
            cancellationToken: cancellationToken
        );
    };
});

// Resource attributes are emitted on every span/metric/log so the OTLP
// receiver (Grafana LGTM in dev, anything OTLP-compatible in prod) can
// segment by service + environment + host. Picked up automatically by
// the Serilog OTLP sink too — it shares the OTEL_RESOURCE_ATTRIBUTES env
// var convention. Override the OTLP endpoint with OTEL_EXPORTER_OTLP_ENDPOINT;
// default is http://localhost:4317 (the LGTM container's OTLP gRPC port).
var otelServiceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(r =>
        r.AddService(serviceName: "pam-api", serviceVersion: otelServiceVersion)
            .AddAttributes(
                new KeyValuePair<string, object>[]
                {
                    new("deployment.environment", builder.Environment.EnvironmentName),
                    new("host.name", Environment.MachineName),
                }
            )
    )
    .WithTracing(t =>
        t.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("MassTransit")
            .AddSource("Pam.*")
            .AddOtlpExporter()
    )
    .WithMetrics(m =>
        m.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Pam.*")
            .AddMeter("MassTransit")
            .AddOtlpExporter()
    );

builder
    .Services.AddHealthChecks()
    .AddRedis(
        sp => sp.GetRequiredService<IConnectionMultiplexer>(),
        name: "redis",
        tags: ["ready", "infra:redis"]
    );

var app = builder.Build();

app.UseForwardedHeaders();

// CorrelationId must run before SerilogRequestLogging so the request-log
// line itself carries the id, and before any consumer of LogContext.
app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();

// Vendor SOAP endpoints — SoapCore middleware short-circuits the
// pipeline on a path match. Mounted BEFORE UseAuthentication so vendor
// traffic skips the bearer-auth fallback policy; each vendor adapter
// authenticates via the SOAP body (systemID + systemPassword), not a
// PAM JWT.
app.UseIngestSoapEndpoints();

app.UseAuthentication();
app.UseAuthorization();
app.MapCarter();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(opt => opt.WithTitle("PAM API").WithTheme(ScalarTheme.BluePlanet))
        .AllowAnonymous();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready", StringComparer.Ordinal) }
    )
    .AllowAnonymous();
app.MapHealthChecks(
        "/health/operators",
        new HealthCheckOptions
        {
            Predicate = h => h.Tags.Contains("module:operators", StringComparer.Ordinal),
        }
    )
    .AllowAnonymous();

await app.Services.UseIdentityModuleAsync();
await app.Services.UseOperatorsModuleAsync();
await app.Services.UsePlayersModuleAsync();
await app.Services.UseWalletModuleAsync();
await app.Services.UseAuditModuleAsync();
await app.Services.UseIngestModuleAsync();

// Shared messaging schema (MassTransit inbox/outbox tables). Applies the
// migration that creates the "messaging" schema; module migrations that
// previously held per-module outbox tables drop those in a paired
// RemoveOutboxTables migration. Order doesn't matter — the schemas are
// independent — but kept last so failures here surface after the modules
// have logged their own migration success.
await app.Services.UsePamMessagingAsync();

await app.RunAsync();

// Exposes the synthesized top-level-program type so
// WebApplicationFactory<Program> in Pam.IntegrationTests can bootstrap
// the host. Without this the compiler-generated Program class is internal
// and the test project can't reference it.
#pragma warning disable S1118 // partial class for top-level statements doesn't need a constructor
public partial class Program;
#pragma warning restore S1118

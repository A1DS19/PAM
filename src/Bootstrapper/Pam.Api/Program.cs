using System.Threading.RateLimiting;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pam.Api.Infrastructure.Infisical;
using Pam.Api.Security;
using Pam.Players;
using Pam.Shared.Exceptions.Handlers;
using Pam.Shared.Extensions;
using Pam.Shared.Messaging.Extensions;
using Pam.Shared.Security;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Infisical: opt-in secret store. No-op when env vars aren't set, so local
// dev keeps using appsettings.{env}.json. Inserted last so it overrides
// every other configuration source.
builder.Configuration.AddInfisical(o =>
{
    o.Host = builder.Configuration["Infisical:Host"];
    o.ProjectId = builder.Configuration["Infisical:ProjectId"];
    o.Environment = builder.Configuration["Infisical:Environment"] ?? "dev";
    o.SecretPath = builder.Configuration["Infisical:SecretPath"] ?? "/";
    o.ClientId = System.Environment.GetEnvironmentVariable("INFISICAL_CLIENT_ID");
    o.ClientSecret = System.Environment.GetEnvironmentVariable("INFISICAL_CLIENT_SECRET");
    o.Optional = true;
});

builder.Host.UseSerilog(
    (ctx, _, cfg) =>
        cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", "pam-api")
            .Enrich.WithProperty("env", ctx.HostingEnvironment.EnvironmentName)
);

var moduleAssemblies = new[] { typeof(PlayersModule).Assembly };

builder.Services.AddPamShared();
builder.Services.AddPamMediatR(moduleAssemblies);
builder.Services.AddPamMassTransit(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();

builder.Services.AddPlayersModule(builder.Configuration);

builder.Services.AddCarter(new DependencyContextAssemblyCatalog(moduleAssemblies));
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<CustomExceptionHandler>();
builder.Services.AddProblemDetails();

var kcBase =
    builder.Configuration["Keycloak:AuthServerUrl"]?.TrimEnd('/')
    ?? throw new InvalidOperationException("Keycloak:AuthServerUrl is not configured");
var playersRealm = builder.Configuration["Keycloak:PlayersRealm"] ?? "players";

builder
    .Services.AddAuthentication()
    .AddJwtBearer(
        "players",
        o =>
        {
            o.Authority = $"{kcBase}/realms/{playersRealm}";
            o.Audience = "pam-player-api";
            o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        }
    );

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("Player", p => p.AddAuthenticationSchemes("players").RequireAuthenticatedUser());

    // Fallback denies anonymous on any endpoint that does not opt in to
    // .AllowAnonymous(). Forces public endpoints to be explicit.
    opt.FallbackPolicy = new AuthorizationPolicyBuilder("players")
        .RequireAuthenticatedUser()
        .Build();
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

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static string ClientPartitionKey(HttpContext ctx)
    {
        // Prefer authenticated identity; fall back to forwarded client IP.
        // UseForwardedHeaders runs before this, so RemoteIpAddress reflects
        // X-Forwarded-For when configured.
        var playerId = ctx.User.FindFirst("player_id")?.Value;
        if (!string.IsNullOrEmpty(playerId))
        {
            return $"player:{playerId}";
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

    o.AddPolicy(
        "auth-sensitive",
        ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                ClientPartitionKey(ctx),
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 5,
                    QueueLimit = 0,
                }
            )
    );

    o.AddPolicy(
        "api-default",
        ctx =>
            RateLimitPartition.GetSlidingWindowLimiter(
                ClientPartitionKey(ctx),
                _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromSeconds(30),
                    SegmentsPerWindow = 6,
                    PermitLimit = 100,
                    QueueLimit = 0,
                }
            )
    );

    o.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiter")
            .LogWarning(
                "Rate limit exceeded: Path={Path} Method={Method} RemoteIp={RemoteIp}",
                context.HttpContext.Request.Path,
                context.HttpContext.Request.Method,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            );

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { title = "Too Many Requests", status = 429 },
            cancellationToken: cancellationToken
        );
    };
});

builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("pam-api"))
    .WithTracing(t =>
        t.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("Npgsql")
            .AddSource("Pam.*")
    )
    .WithMetrics(m =>
        m.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Pam.*")
    );

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapCarter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(opt => opt.WithTitle("PAM API").WithTheme(ScalarTheme.Purple))
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
        "/health/players",
        new HealthCheckOptions
        {
            Predicate = h => h.Tags.Contains("module:player", StringComparer.Ordinal),
        }
    )
    .AllowAnonymous();

app.UsePlayersModule();

await app.RunAsync();

using System.Threading.RateLimiting;
using Carter;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pam.Api.Security;
using Pam.Players;
using Pam.Shared.Exceptions.Handlers;
using Pam.Shared.Extensions;
using Pam.Shared.Messaging.Extensions;
using Pam.Shared.Security;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddFixedWindowLimiter(
        "auth-sensitive",
        opt =>
        {
            opt.Window = TimeSpan.FromMinutes(1);
            opt.PermitLimit = 5;
            opt.QueueLimit = 0;
        }
    );

    o.AddPolicy(
        "api-default",
        ctx =>
            RateLimitPartition.GetSlidingWindowLimiter(
                ctx.User.FindFirst("player_id")?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString()
                    ?? "anon",
                _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromSeconds(30),
                    SegmentsPerWindow = 6,
                    PermitLimit = 100,
                    QueueLimit = 0,
                }
            )
    );
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

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapCarter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt => opt.WithTitle("PAM API").WithTheme(ScalarTheme.Purple));
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready", StringComparer.Ordinal) }
);
app.MapHealthChecks(
    "/health/players",
    new HealthCheckOptions
    {
        Predicate = h => h.Tags.Contains("module:player", StringComparer.Ordinal),
    }
);

app.UsePlayersModule();

await app.RunAsync();

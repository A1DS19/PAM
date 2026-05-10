using System.Threading.RateLimiting;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pam.Identity;
using Pam.Identity.Contracts.Permissions;
using Pam.Notifications;
using Pam.Operators;
using Pam.Shared.Exceptions.Handlers;
using Pam.Shared.Extensions;
using Pam.Shared.Messaging.Extensions;
using Scalar.AspNetCore;
using Serilog;

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
    typeof(NotificationsModule).Assembly,
    typeof(OperatorsModule).Assembly,
};

builder.Services.AddPamShared();
builder.Services.AddPamMediatR(moduleAssemblies);
// Consumer assemblies for MassTransit auto-discovery — Pam.Notifications
// houses the integration-event subscribers (welcome emails, transactional
// receipts, ...). Pam.Identity/Pam.Operators don't consume events yet.
builder.Services.AddPamMassTransit(
    builder.Configuration,
    typeof(NotificationsModule).Assembly
);

builder.Services.AddHttpContextAccessor();

// Notifications first — Identity's forgot-password / send-confirmation-email
// handlers resolve IEmailSender from Notifications during DI graph building.
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddOperatorsModule(builder.Configuration);

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
app.UseCors();
app.UseRateLimiter();
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

await app.RunAsync();

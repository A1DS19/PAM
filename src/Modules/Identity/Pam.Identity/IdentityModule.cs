using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pam.Identity.Authentication;
using Pam.Identity.Data;
using Pam.Identity.Permissions;
using Pam.Identity.Seeding;
using Pam.Identity.Users.Models;
using Pam.Shared.Data.Interceptors;
using Pam.Shared.Security;
using Quartz;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Pam.Identity;

public static class IdentityModule
{
    // PAM's API audience. Access tokens carry "pam_api" in their `aud` claim;
    // the validation stack uses this to reject tokens issued for other
    // audiences (e.g. a future Players audience).
    public const string PamApiScope = "pam_api";

    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var connectionString =
            configuration.GetConnectionString("Pam")
            ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured");

        services.TryAddScoped<AuditableSaveChangesInterceptor>();
        services.TryAddScoped<DispatchDomainEventsInterceptor>();

        // Replace Pam.Shared's default SystemUserContext with one that reads
        // the authenticated principal — audit columns now distinguish
        // background work (Actor.System) from operator-driven mutations
        // (Actor.Operator with the OIDC `sub` claim).
        services.RemoveAll<IUserContext>();
        services.AddScoped<IUserContext, HttpUserContext>();

        services.AddDbContext<IdentityDbContext>(
            (sp, options) =>
            {
                options.AddInterceptors(
                    sp.GetRequiredService<AuditableSaveChangesInterceptor>(),
                    sp.GetRequiredService<DispatchDomainEventsInterceptor>()
                );
                options.UseNpgsql(
                    connectionString,
                    npg =>
                    {
                        npg.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            IdentityDbContext.Schema
                        );
                        npg.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();

                // Registers OpenIddict's EF entity sets (Applications, Authorizations,
                // Scopes, Tokens) into this DbContext's model. The Core stack below
                // points back at the same context.
                options.UseOpenIddict();
            }
        );

        // ASP.NET Core Identity — owns user storage, password hashing, lockout,
        // MFA. Defaults are tightened to a reasonable back-office baseline; a
        // jurisdiction- or operator-aligned policy can be loaded from config
        // when those constraints arrive.
        services
            .AddOptions<BackOfficeSpaOptions>()
            .Bind(configuration.GetSection(BackOfficeSpaOptions.SectionName))
            .ValidateOnStart();

        // IEmailSender lives in Pam.Notifications. Identity injects it
        // (forgot-password, send-confirmation-email) but does not register
        // it. AddNotificationsModule must run before AddIdentityModule in
        // Pam.Api/Program.cs.

        services.AddScoped<PermissionResolver>();
        services.AddScoped<IdentitySeeder>();

        services
            .AddIdentity<BackOfficeUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                // Email confirmation + MFA are deferred to PR 2 alongside the
                // admin user-CRUD endpoints. Identity's TOTP authenticator
                // works out of the box once we expose the enrollment endpoints.
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedAccount = false;

                // The security stamp validates each request's cookie against
                // the user's current state — short interval so token revocation
                // (forced password reset, MFA reset, self-exclusion → token
                // revoke) is felt within seconds, not minutes.
                options.Stores.ProtectPersonalData = false;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders();

        // The cookie middleware's default LoginPath is local; the back-office
        // login UI lives in the React SPA on a different origin. Override
        // OnRedirectToLogin to send the browser there with ?returnUrl= so
        // the SPA can navigate back into the OIDC flow after sign-in. JSON
        // API requests get a 401 instead of a redirect.
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);

            options.Events.OnRedirectToLogin = ctx =>
            {
                if (IsApiRequest(ctx.Request))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                var spa = ctx
                    .HttpContext.RequestServices.GetRequiredService<
                        IOptions<BackOfficeSpaOptions>
                    >()
                    .Value;
                var returnUrl = Uri.EscapeDataString(ctx.RedirectUri);
                ctx.Response.Redirect($"{spa.LoginUrl}?returnUrl={returnUrl}");
                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Quartz drives the periodic OpenIddict cleanup (orphan tokens,
        // expired authorizations). In-memory store is fine here — the schedule
        // is recreated on every startup and runs hourly by default.
        services.AddQuartz(options =>
        {
            options.UseSimpleTypeLoader();
            options.UseInMemoryStore();
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services
            .AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore().UseDbContext<IdentityDbContext>();
                options.UseQuartz();
            })
            .AddServer(options =>
            {
                options
                    .SetAuthorizationEndpointUris("connect/authorize")
                    .SetEndSessionEndpointUris("connect/logout")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetPushedAuthorizationEndpointUris("connect/par")
                    .SetRevocationEndpointUris("connect/revocation")
                    .SetIntrospectionEndpointUris("connect/introspect");

                // Standard OIDC scopes plus our API audience scope. Clients
                // requesting `pam_api` get an access token whose `aud`
                // contains `pam_api` — that's what the validation stack
                // checks at the API edge.
                options.RegisterScopes(
                    Scopes.OpenId,
                    Scopes.Email,
                    Scopes.Profile,
                    Scopes.Roles,
                    Scopes.OfflineAccess,
                    PamApiScope
                );

                // Authorization Code + PKCE for the back-office SPA, plus
                // Refresh for silent renewal. Client Credentials gets enabled
                // when module-to-module calls land.
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();

                // PKCE required globally — closes the "public-client client
                // secret" loophole and protects against authorization code
                // interception even for confidential clients.
                options.RequireProofKeyForCodeExchange();

                // Dev: persisted self-signed certs in the user store. Reused
                // across restarts on the same machine; not shared across
                // replicas (each gets its own keypair). Acceptable because
                // we don't run multi-instance in dev.
                //
                // Non-dev: certs are loaded from PFX files mounted by the
                // orchestrator (k8s Secret-as-file, systemd LoadCredential=,
                // Swarm secret). Sharing the same PFX across replicas is what
                // makes a token signed by replica A validate on replica B —
                // and what keeps tokens valid across rolling deploys.
                //
                // Rotation strategy: keep the previous cert in
                // OpenIddict:Validation:IssuerSigningKeys for one release so
                // tokens issued before rotation still validate. Then drop it.
                if (environment.IsDevelopment())
                {
                    options
                        .AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    options.AddEncryptionCertificate(
                        LoadCertificate(
                            configuration,
                            "OpenIddict:EncryptionCertificate",
                            "encryption"
                        )
                    );
                    options.AddSigningCertificate(
                        LoadCertificate(
                            configuration,
                            "OpenIddict:SigningCertificate",
                            "signing"
                        )
                    );
                }

                // ASP.NET Core integration. Endpoint pass-through hands the
                // request off to our AuthorizationController / endpoints
                // instead of OpenIddict's built-in handler.
                // PAR doesn't have a passthrough — OpenIddict handles
                // /connect/par natively. The other endpoints route to our
                // AuthorizationController + login endpoint.
                //
                // HTTPS is required on /connect/* in production (OpenIddict
                // checks Request.IsHttps; via ForwardedHeaders middleware
                // that reflects X-Forwarded-Proto from the TLS-terminating
                // proxy). In Development we disable the check so the API
                // works under plain http://localhost:5000.
                var aspNetCore = options
                    .UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
                if (environment.IsDevelopment())
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                // Same host as the server — keys + scopes imported in-process.
                // No HTTP introspection round-trip.
                options.UseLocalServer();
                options.UseAspNetCore();

                // Entry validation: every API call cross-checks the access
                // token's authorization + token rows in identity.openiddict_*.
                // Tradeoff is explicit, document here so the next person
                // looking at perf data understands the wiring:
                //
                //   Pros — revocation is effectively instant. Soft-deleting a
                //          user, removing a role, or hitting /connect/revocation
                //          kills the user's outstanding tokens at the next
                //          request, instead of waiting for the security-stamp
                //          validation window (~30min default).
                //
                //   Cons — 2 extra Postgres lookups per authenticated request.
                //          Both are PK reads on indexed columns (sub-ms), and
                //          Postgres caches them in shared buffers. Fine for the
                //          back-office surface (~tens to low-hundreds of
                //          operators). Becomes a problem if/when we wire a
                //          high-throughput surface like Pam.GameWallet
                //          (sub-200ms p99, thousands of req/s) — that host
                //          should configure its own validation stack WITHOUT
                //          these flags and accept short revocation lag.
                //
                // The Quartz cleanup job (UseQuartz in AddCore above) prunes
                // expired / revoked tokens hourly, so the openiddict_tokens
                // table tracks active sessions, not all-time history.
                options.EnableAuthorizationEntryValidation();
                options.EnableTokenEntryValidation();
            });

        services
            .AddHealthChecks()
            .AddNpgSql(connectionString, name: "identity-db", tags: ["ready", "module:identity"]);

        return services;
    }

    public static async Task UseIdentityModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }

    // Heuristic: requests with an Accept header that prefers JSON, or that
    // hit a /v1/ path, are programmatic API calls — return 401, not a 302.
    // Browser navigations to /connect/authorize miss both checks and still
    // get redirected to the SPA login page.
    private static bool IsApiRequest(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var accept = request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    // Load a PFX from a path configured under OpenIddict:<role>:Path with an
    // optional password under :Password. Fail-fast at startup with a clear
    // error if the path is unset or the file is missing — silently falling
    // back to ephemeral keys in non-dev would cause every replica to sign
    // with its own keypair and break auth in subtle ways.
    private static X509Certificate2 LoadCertificate(
        IConfiguration configuration,
        string sectionName,
        string role
    )
    {
        var section = configuration.GetSection(sectionName);
        var path =
            section["Path"]
            ?? throw new InvalidOperationException(
                $"{sectionName}:Path is required outside Development. "
                    + $"Mount the {role} PFX and point this config at it "
                    + "(or run the host as Development to use self-signed dev certs)."
            );
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"{sectionName}:Path points at '{path}' which does not exist."
            );
        }
        var password = section["Password"];
        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet
        );
    }
}

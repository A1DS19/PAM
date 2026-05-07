using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pam.Identity.Data;
using Pam.Identity.Permissions;
using Pam.Identity.Seeding;
using Pam.Identity.Users.Models;
using Pam.Shared.Data.Interceptors;
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
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Pam")
            ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured");

        services.TryAddScoped<AuditableSaveChangesInterceptor>();
        services.TryAddScoped<DispatchDomainEventsInterceptor>();

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
                var spa = ctx.HttpContext.RequestServices
                    .GetRequiredService<IOptions<BackOfficeSpaOptions>>()
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
                    .SetPushedAuthorizationEndpointUris("connect/par");

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

                // Dev: persisted self-signed certs in the user store, reused
                // across restarts. Prod: AddSigningCertificate(thumbprint) +
                // AddEncryptionCertificate(thumbprint) from env-injected
                // certificates (separate from HTTPS cert). See ROADMAP.
                options
                    .AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();

                // ASP.NET Core integration. Endpoint pass-through hands the
                // request off to our AuthorizationController / endpoints
                // instead of OpenIddict's built-in handler.
                // PAR doesn't have a passthrough — OpenIddict handles
                // /connect/par natively. The other endpoints route to our
                // AuthorizationController + login endpoint.
                options
                    .UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
            })
            .AddValidation(options =>
            {
                // Same host as the server — keys + scopes imported in-process.
                // No HTTP introspection round-trip; revocation visibility is
                // bounded by token TTL until Limits ships and we enable
                // EnableAuthorizationEntryValidation here.
                options.UseLocalServer();
                options.UseAspNetCore();
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
}

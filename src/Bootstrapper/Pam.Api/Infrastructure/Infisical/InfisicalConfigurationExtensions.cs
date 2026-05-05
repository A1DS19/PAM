using Microsoft.Extensions.Configuration;

namespace Pam.Api.Infrastructure.Infisical;

public static class InfisicalConfigurationExtensions
{
    /// <summary>
    /// Adds an Infisical-backed configuration source. Inserted last so it
    /// overrides appsettings/user-secrets/env-vars by default.
    /// </summary>
    public static IConfigurationBuilder AddInfisical(
        this IConfigurationBuilder builder,
        Action<InfisicalOptions> configure)
    {
        var opts = new InfisicalOptions();
        configure(opts);
        builder.Add(new InfisicalSecretsConfigurationSource { Options = opts });
        return builder;
    }
}

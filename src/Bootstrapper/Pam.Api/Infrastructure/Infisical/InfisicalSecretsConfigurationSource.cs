using Microsoft.Extensions.Configuration;

namespace Pam.Api.Infrastructure.Infisical;

public sealed class InfisicalSecretsConfigurationSource : IConfigurationSource
{
    public required InfisicalOptions Options { get; init; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new InfisicalSecretsConfigurationProvider(Options);
    }
}

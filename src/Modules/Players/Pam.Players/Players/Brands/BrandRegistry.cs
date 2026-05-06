using Microsoft.Extensions.Options;
using Pam.Players.Infrastructure.Zitadel;

namespace Pam.Players.Players.Brands;

public sealed class BrandRegistry(ZitadelRuntimeState state, IOptions<BrandRegistryOptions> options)
    : IBrandRegistry
{
    private readonly BrandRegistryOptions _options = options.Value;

    public bool IsRegistered(string brandId) =>
        _options.BrandIds.Contains(brandId, StringComparer.Ordinal);

    public string GetOrgId(string brandId)
    {
        if (!IsRegistered(brandId))
        {
            throw new KeyNotFoundException($"Brand '{brandId}' is not in Brands:BrandIds.");
        }
        if (!state.BrandOrgIds.TryGetValue(brandId, out var orgId))
        {
            throw new InvalidOperationException(
                $"Brand '{brandId}' has no resolved ZITADEL Org id — bootstrap did not run."
            );
        }
        return orgId;
    }
}

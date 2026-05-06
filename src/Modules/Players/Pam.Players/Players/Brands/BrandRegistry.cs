using Microsoft.Extensions.Options;

namespace Pam.Players.Players.Brands;

public sealed class BrandRegistry(IOptions<BrandRegistryOptions> options) : IBrandRegistry
{
    private readonly BrandRegistryOptions _options = options.Value;

    public bool IsRegistered(string brandId) => _options.Map.ContainsKey(brandId);

    public string GetOrgId(string brandId) =>
        _options.Map.TryGetValue(brandId, out var entry)
            ? entry.ZitadelOrgId
            : throw new KeyNotFoundException($"Brand '{brandId}' not registered.");
}

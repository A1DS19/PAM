namespace Pam.Players.Players.Brands;

public sealed class BrandRegistryOptions
{
    public const string SectionName = "Brands";

    public string Default { get; init; } = "betanything-eu";

    public IDictionary<string, BrandEntry> Map { get; init; } =
        new Dictionary<string, BrandEntry>(StringComparer.Ordinal);
}

public sealed class BrandEntry
{
    public string ZitadelOrgId { get; init; } = default!;
}

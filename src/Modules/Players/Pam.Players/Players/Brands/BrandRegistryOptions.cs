namespace Pam.Players.Players.Brands;

public sealed class BrandRegistryOptions
{
    public const string SectionName = "Brands";

    public string Default { get; init; } = "betanything-eu";

    public IList<string> BrandIds { get; init; } =
        new List<string> { "betanything-eu", "betanything-latam-stub" };
}

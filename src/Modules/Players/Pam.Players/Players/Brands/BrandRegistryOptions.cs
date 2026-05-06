using System.Diagnostics.CodeAnalysis;

namespace Pam.Players.Players.Brands;

public sealed class BrandRegistryOptions
{
    public const string SectionName = "Brands";

    public string Default { get; init; } = "betanything-eu";

    // Must be string[] (not IList<string>) — IConfiguration.Bind appends
    // configured values to a non-empty default list instead of replacing it,
    // producing duplicate brand entries. Arrays are replaced wholesale.
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Required for correct IConfiguration binding semantics."
    )]
    public string[] BrandIds { get; init; } = ["betanything-eu", "betanything-latam-stub"];
}

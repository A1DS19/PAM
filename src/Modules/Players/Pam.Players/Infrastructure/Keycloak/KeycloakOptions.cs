namespace Pam.Players.Infrastructure.Keycloak;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string AuthServerUrl { get; init; } = default!;

    public string PlayersRealm { get; init; } = "players";

    public string AdminRealm { get; init; } = "master";

    public string AdminClientId { get; init; } = "admin-cli";

    public string? AdminClientSecret { get; init; }

    public string? AdminUsername { get; init; }

    public string? AdminPassword { get; init; }
}

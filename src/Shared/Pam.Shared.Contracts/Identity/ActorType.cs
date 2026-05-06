namespace Pam.Shared.Contracts.Identity;

public enum ActorType
{
    /// Background jobs, migrations, seeders, anything not driven by a user.
    System,

    /// A registered Player acting on their own account.
    Player,

    /// A back-office user from the operators realm (when it lands).
    Operator,

    /// A trusted machine identity (game-wallet ingress, internal service-to-service).
    Service,

    /// Authenticated request with no resolvable identity, or no auth at all.
    Anonymous,
}

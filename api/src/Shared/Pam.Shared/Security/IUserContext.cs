using Pam.Shared.Contracts.Identity;

namespace Pam.Shared.Security;

public interface IUserContext
{
    /// The actor responsible for the current operation. Never null —
    /// background work uses Actor.System, unauthenticated requests use
    /// Actor.Anonymous.
    Actor Current { get; }

    string DisplayName { get; }
}

public sealed class SystemUserContext : IUserContext
{
    public Actor Current => Actor.System;

    public string DisplayName => "system";
}

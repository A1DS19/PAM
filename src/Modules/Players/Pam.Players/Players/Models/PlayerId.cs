using Pam.Shared.Contracts.Identity;

namespace Pam.Players.Players.Models;

public readonly record struct PlayerId(Guid Value)
{
    public static PlayerId New() => new(PamIds.New());

    public override string ToString() => Value.ToString();
}

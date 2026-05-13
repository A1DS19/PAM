namespace Pam.Shared.Contracts.Identity;

public sealed record Actor(ActorType Type, string Id)
{
    public static readonly Actor System = new(ActorType.System, "system");

    public static readonly Actor Anonymous = new(ActorType.Anonymous, "anonymous");

    public override string ToString() => $"{Type}:{Id}";
}

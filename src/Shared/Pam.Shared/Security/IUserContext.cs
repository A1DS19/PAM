namespace Pam.Shared.Security;

public interface IUserContext
{
    string UserId { get; }

    string DisplayName { get; }
}

public sealed class SystemUserContext : IUserContext
{
    public string UserId => "system";

    public string DisplayName => "system";
}

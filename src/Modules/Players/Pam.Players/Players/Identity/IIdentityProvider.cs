namespace Pam.Players.Players.Identity;

public readonly record struct IdentityUserId(string Value)
{
    public override string ToString() => Value;
}

public sealed record CreateIdentityUser(
    string BrandId,
    string Email,
    string FirstName,
    string LastName,
    string Password,
    bool RequireEmailVerify
);

public interface IIdentityProvider
{
    Task<IdentityUserId> CreateUserAsync(CreateIdentityUser input, CancellationToken ct);

    Task SendVerifyEmailAsync(IdentityUserId id, CancellationToken ct);

    Task DeleteUserAsync(IdentityUserId id, CancellationToken ct);
}

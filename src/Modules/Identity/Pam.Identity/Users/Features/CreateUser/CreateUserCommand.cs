using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.CreateUser;

[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record CreateUserCommand(
    string Email,
    string Password,
    Guid? BrandId,
    IReadOnlyList<string> Roles
) : ICommand<Guid>;

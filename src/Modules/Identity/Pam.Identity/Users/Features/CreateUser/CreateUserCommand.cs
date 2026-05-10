using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.CreateUser;

public sealed record CreateUserCommand(
    string Email,
    string Password,
    Guid? BrandId,
    IReadOnlyList<string> Roles
) : ICommand<Guid>;

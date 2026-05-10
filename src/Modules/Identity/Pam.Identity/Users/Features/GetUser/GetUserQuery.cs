using Pam.Identity.Contracts.Users.Dtos;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.GetUser;

public sealed record GetUserQuery(Guid UserId) : IQuery<BackOfficeUserDto>;

using Pam.Identity.Contracts.Users.Dtos;
using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.GetUser;

[Cache(durationMinutes: 5, keyPattern: "identity:user:{UserId}")]
public sealed record GetUserQuery(Guid UserId) : IQuery<BackOfficeUserDto>;

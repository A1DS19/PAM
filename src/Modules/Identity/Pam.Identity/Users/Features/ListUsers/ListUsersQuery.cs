using Pam.Identity.Contracts.Users.Dtos;
using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.ListUsers;

// Page is 1-based (operator-friendly URL: ?page=1&pageSize=50).
// Filters are optional and AND-combined: BrandId, Role, LockedOut?.
[Cache(
    durationMinutes: 2,
    keyPattern: "identity:users:list:p={Page}:s={PageSize}:b={BrandId}:r={Role}:l={LockedOut}"
)]
public sealed record ListUsersQuery(
    int Page,
    int PageSize,
    Guid? BrandId,
    string? Role,
    bool? LockedOut
) : IQuery<ListUsersResult>;

public sealed record ListUsersResult(
    IReadOnlyList<BackOfficeUserDto> Items,
    long Total,
    int Page,
    int PageSize
);

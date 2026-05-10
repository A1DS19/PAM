using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pam.Identity.Contracts.Users.Dtos;
using Pam.Identity.Data;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.ListUsers;

// Single round-trip plan: paged user query, then a single
// `user-roles JOIN roles` query for the page's worth of users. The naive
// per-user GetRolesAsync would be N+1; the global query filter on BrandId
// (when it lands) will compose with the explicit BrandId filter here.
public sealed class ListUsersHandler(
    IdentityDbContext db,
    RoleManager<IdentityRole<Guid>> roleManager
) : IQueryHandler<ListUsersQuery, ListUsersResult>
{
    public async Task<ListUsersResult> Handle(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking().AsQueryable();
        if (query.BrandId is { } brandId)
        {
            users = users.Where(u => u.BrandId == brandId);
        }
        if (query.LockedOut is true)
        {
            var now = DateTimeOffset.UtcNow;
            users = users.Where(u => u.LockoutEnd != null && u.LockoutEnd > now);
        }
        if (query.LockedOut is false)
        {
            var now = DateTimeOffset.UtcNow;
            users = users.Where(u => u.LockoutEnd == null || u.LockoutEnd <= now);
        }

        if (query.Role is { } roleName)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                return new ListUsersResult([], 0, query.Page, query.PageSize);
            }
            users = users.Where(u =>
                db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == role.Id)
            );
        }

        var total = await users.LongCountAsync(cancellationToken);

        var page = await users
            .OrderBy(u => u.Email)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var userIds = page.Select(u => u.Id).ToList();
        var roleMap = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where userIds.Contains(ur.UserId)
            select new { ur.UserId, r.Name }
        ).ToListAsync(cancellationToken);

        var rolesByUser = roleMap
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name ?? string.Empty).ToList());

        var items = page.Select(u => new BackOfficeUserDto(
                Id: u.Id,
                Email: u.Email ?? string.Empty,
                BrandId: u.BrandId,
                EmailConfirmed: u.EmailConfirmed,
                TwoFactorEnabled: u.TwoFactorEnabled,
                LockoutEnabled: u.LockoutEnabled,
                LockoutEnd: u.LockoutEnd,
                Roles: rolesByUser.TryGetValue(u.Id, out var r) ? r : [],
                CreatedAt: u.CreatedAt,
                LastModifiedAt: u.LastModifiedAt
            ))
            .ToList();

        return new ListUsersResult(items, total, query.Page, query.PageSize);
    }
}

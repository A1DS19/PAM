using Microsoft.AspNetCore.Http;
using Pam.Shared.Security;

namespace Pam.Api.Security;

public sealed class HttpUserContext(IHttpContextAccessor http) : IUserContext
{
    public string UserId =>
        http.HttpContext?.User.FindFirst("sub")?.Value
        ?? http.HttpContext?.User.FindFirst("player_id")?.Value
        ?? "anonymous";

    public string DisplayName =>
        http.HttpContext?.User.FindFirst("preferred_username")?.Value
        ?? http.HttpContext?.User.FindFirst("name")?.Value
        ?? UserId;
}

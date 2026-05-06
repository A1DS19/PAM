using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Pam.Players.Infrastructure.Zitadel;

public sealed class ZitadelTokenHandler(IOptions<ZitadelOptions> options) : DelegatingHandler
{
    private readonly ZitadelOptions _options = options.Value;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AdminPat);
        return base.SendAsync(request, cancellationToken);
    }
}

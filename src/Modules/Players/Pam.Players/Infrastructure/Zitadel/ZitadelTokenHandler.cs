using System.Net.Http.Headers;

namespace Pam.Players.Infrastructure.Zitadel;

public sealed class ZitadelTokenHandler(ZitadelRuntimeState state) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.AdminPat);
        return base.SendAsync(request, cancellationToken);
    }
}

using System.Net.Http.Headers;

namespace ShortP2P.MessengerServer.Http;

/// <summary>Adds <c>Authorization: Bearer</c> from <see cref="IMessengerServerSession"/> when a token is present.</summary>
public sealed class MessengerServerBearerHandler(IMessengerServerSession session) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = session.AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}

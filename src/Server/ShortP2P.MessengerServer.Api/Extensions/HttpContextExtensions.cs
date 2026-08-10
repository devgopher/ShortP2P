using System.Security.Claims;
using ShortP2P.MessengerServer.Auth;
using ShortP2P.MessengerServer.UseCases;

namespace ShortP2P.MessengerServer.Api.Http;

public static class HttpContextExtensions
{
    public static string RequireNetworkId(this HttpContext http)
    {
        var networkId =
            http.User.FindFirstValue(JwtAuthTokenService.NetworkIdClaimType)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(networkId))
            throw UseCaseException.Unauthorized("Missing network id claim.");

        return networkId;
    }
}

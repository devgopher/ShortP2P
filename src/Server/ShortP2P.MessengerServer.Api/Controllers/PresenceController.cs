using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Api.Controllers;
using ShortP2P.MessengerServer.Api.Http;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases.Presence;
using ShortP2P.MessengerServer.UseCases;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Authorize]
[Route(ApiRoutes.Prefix)]
public sealed class PresenceController(
    KeepAliveUseCase keepAliveUseCase) : ControllerBase
{
    [HttpPost("keepalive")]
    public async Task<IActionResult> KeepAliveAsync(
        [FromBody] KeepAliveRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.RequireNetworkId();
            if (!string.Equals(request.NetworkId?.Trim(), caller, StringComparison.Ordinal))
                throw UseCaseException.Unauthorized("networkId must match the authenticated client.");

            await keepAliveUseCase
                .ExecuteAsync(new KeepAliveCommand(caller), cancellationToken)
                .ConfigureAwait(false);

            return NoContent();
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }
}


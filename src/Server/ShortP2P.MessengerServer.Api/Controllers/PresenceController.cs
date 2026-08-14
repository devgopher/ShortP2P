using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Api.Extensions;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Presence;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Authorize]
[Route($"{ApiRoutes.Prefix}")]
public sealed class PresenceController(
    KeepAliveUseCase keepAliveUseCase,
    GetClientPresencesUseCase getClientPresencesUseCase) : ControllerBase
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

    [HttpGet("clients")]
    public async Task<IActionResult> GetClientsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = HttpContext.RequireNetworkId();
            var list = await getClientPresencesUseCase
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return Ok(list.Select(x => x.ToDto()).ToArray());
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }
}


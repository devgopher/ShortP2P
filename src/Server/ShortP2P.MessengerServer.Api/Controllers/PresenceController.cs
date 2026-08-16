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
public sealed class PresenceController(GetClientPresencesUseCase getClientPresencesUseCase) : ControllerBase
{
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

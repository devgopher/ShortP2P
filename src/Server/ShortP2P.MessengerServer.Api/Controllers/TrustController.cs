using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Api.Extensions;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Trust;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Authorize]
[Route($"{ApiRoutes.Prefix}/trust")]
public sealed class TrustController(
    AskRatingUseCase askRatingUseCase,
    AskServersUseCase askServersUseCase,
    ClaimServerUseCase claimServerUseCase) : ControllerBase
{
    [HttpGet("ask-rating")]
    public async Task<IActionResult> AskRatingAsync(
        [FromQuery] string serverIp,
        [FromQuery] int serverPort,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = HttpContext.RequireNetworkId();
            var list = await askRatingUseCase
                .ExecuteAsync(serverIp, serverPort, cancellationToken)
                .ConfigureAwait(false);
            return Ok(list.Select(x => new ServerRatingDto
            {
                ServerIp = x.ServerIp,
                ServerPort = x.ServerPort,
                Rating = x.Rating
            }).ToArray());
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }

    [HttpGet("ask-servers")]
    public async Task<IActionResult> AskServersAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = HttpContext.RequireNetworkId();
            var list = await askServersUseCase.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return Ok(list.Select(x => new ServerRatingDto
            {
                ServerIp = x.ServerIp,
                ServerPort = x.ServerPort,
                Rating = x.Rating
            }).ToArray());
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }

    [HttpPost("claim")]
    public async Task<IActionResult> ClaimServerAsync(
        [FromBody] ClaimServerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.RequireNetworkId();
            await claimServerUseCase
                .ExecuteAsync(caller, request.ServerIp, request.ServerPort, request.Reason, cancellationToken)
                .ConfigureAwait(false);
            return StatusCode(StatusCodes.Status202Accepted);
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }
}

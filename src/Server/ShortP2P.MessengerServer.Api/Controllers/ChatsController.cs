using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Api.Controllers;
using ShortP2P.MessengerServer.Api.Http;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Chats;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Authorize]
[Route(ApiRoutes.Prefix + "/chats")]
public sealed class ChatsController(
    GetChatsUseCase getChatsUseCase,
    CreateChatRequestUseCase createChatRequestUseCase,
    GetChatRequestsUseCase getChatRequestsUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetChatsAsync(
        [FromQuery] string? networkId,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.RequireNetworkId();
            var queryNetworkId = string.IsNullOrWhiteSpace(networkId) ? caller : networkId.Trim();
            if (!string.Equals(queryNetworkId, caller, StringComparison.Ordinal))
                throw UseCaseException.Unauthorized("networkId must match the authenticated client.");

            var chats = await getChatsUseCase
                .ExecuteAsync(new GetChatsQuery(caller), cancellationToken)
                .ConfigureAwait(false);

            return Ok(chats.Select(c => c.ToDto()).ToArray());
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }

    [HttpPost("requests")]
    public async Task<IActionResult> CreateChatRequestAsync(
        [FromBody] ChatRequestCreateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.RequireNetworkId();
            await createChatRequestUseCase
                .ExecuteAsync(new CreateChatRequestCommand(caller, request.PublicKey, request.TargetNetworkId), cancellationToken)
                .ConfigureAwait(false);

            return Accepted();
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }

    [HttpGet("requests")]
    public async Task<IActionResult> GetChatRequestsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.RequireNetworkId();
            var list = await getChatRequestsUseCase
                .ExecuteAsync(new GetChatRequestsQuery(caller), cancellationToken)
                .ConfigureAwait(false);

            return Ok(list.Select(x => x.ToDto()).ToArray());
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }
}


using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[RequireHttps]
[Route($"{ApiRoutes.Prefix}/bot_data_flow")]
public sealed class BotDataFlowController : ControllerBase
{
    /// <summary>
    /// Long-poll for encrypted messages from clients to this bot.
    /// Auth via <see cref="BotWaitForIncomeMessagesRequest.BotNetworkId"/> + <see cref="BotWaitForIncomeMessagesRequest.BotKey"/>.
    /// </summary>
    [HttpPost("wait_for_income_messages")]
    [AllowAnonymous]
    public IActionResult WaitForIncomeMessages([FromBody] BotWaitForIncomeMessagesRequest request)
    {
        // Mock: real bot auth + inbox long-poll TBD (empty = timed out with nothing queued).
        return Ok(new BotWaitForIncomeMessagesResponse
        {
            RequestId = request.RequestId,
            Messages = []
        });
    }

    /// <summary>
    /// Deliver encrypted messages from the bot to clients.
    /// Auth via <see cref="BotSendMessagesRequest.BotNetworkId"/> + <see cref="BotSendMessagesRequest.BotKey"/>.
    /// </summary>
    [HttpPost("send_messages")]
    [AllowAnonymous]
    public IActionResult SendMessages([FromBody] BotSendMessagesRequest request)
    {
        // Mock: real bot auth + fan-out TBD.
        return Ok(new BotSendMessagesResponse
        {
            RequestId = request.RequestId
        });
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;

namespace ShortP2P.MessengerServer.Api.Controllers.Bot;

[ApiController]
[RequireHttps]
[Route($"{ApiRoutes.Prefix}/bot_client_interaction")]
public sealed class BotClientInteractionController : ControllerBase
{
    /// <summary>
    /// Long-poll for encrypted messages from bots to this client.
    /// </summary>
    [HttpPost("wait_for_messages")]
    [AllowAnonymous]
    public IActionResult WaitForMessages([FromBody] BotWaitForMessagesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NetworkId))
        {
            return BadRequest();
        }

        // Mock: real client auth + inbox long-poll TBD (empty = timed out with nothing queued).
        return Ok(new BotWaitForMessagesResponse
        {
            Messages = []
        });
    }

    /// <summary>
    /// Deliver one encrypted message from the client to a bot.
    /// </summary>
    [HttpPost("send_message")]
    [AllowAnonymous]
    public IActionResult SendMessage([FromBody] BotSendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId)
            || string.IsNullOrWhiteSpace(request.NetworkId)
            || string.IsNullOrWhiteSpace(request.Message)
            || request.DateTime == default)
        {
            return Ok(new BotSendMessageResponse
            {
                MessageId = request.MessageId ?? string.Empty,
                ErrorCode = 2,
                ErrorMessage = "Wrong input"
            });
        }

        // Mock: real store-and-forward TBD.
        return Ok(new BotSendMessageResponse
        {
            MessageId = request.MessageId,
            ErrorCode = 0,
            ErrorMessage = string.Empty
        });
    }
}

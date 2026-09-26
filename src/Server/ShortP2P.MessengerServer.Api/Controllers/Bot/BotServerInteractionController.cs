using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Controllers.Bot;

[ApiController]
[RequireHttps]
[Route($"{ApiRoutes.Prefix}/bot_server_interaction")]
public sealed class BotServerInteractionController(IBotKeyGenerator botKeyGenerator) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public IActionResult Register([FromBody] BotRegisterRequest request)
    {
        // Mock: persistence / validation TBD.
        var botKey = botKeyGenerator.Generate();

        return StatusCode(StatusCodes.Status201Created, new BotRegisterResponse
        {
            NetworkId = request.NetworkId,
            BotName = request.BotName,
            BotReadableName = request.BotReadableName,
            BotDescription = request.BotDescription,
            BotKey = botKey
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] BotLoginRequest request)
    {
        // Mock: accept any payload; real auth TBD.
        return Ok(new BotLoginResponse
        {
            Token = "mock-bot-token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });
    }

    [HttpPost("remove")]
    [Authorize]
    [AllowAnonymous]
    public IActionResult Remove([FromBody] BotRemoveRequest request)
    {
        // Mock: echo identifiers; real removal TBD.
        return Ok(new BotRemoveResponse
        {
            NetworkId = request.NetworkId,
            BotKey = request.BotKey
        });
    }
}

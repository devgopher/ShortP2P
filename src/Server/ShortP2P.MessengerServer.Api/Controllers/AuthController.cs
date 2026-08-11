using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Auth;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Route(ApiRoutes.Prefix + "/auth")]
public sealed class AuthController(
    RegisterClientUseCase registerUseCase,
    LoginClientUseCase loginUseCase) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await registerUseCase.ExecuteAsync(
                new RegisterClientCommand(request.Nick, request.NetworkId, request.Password),
                cancellationToken).ConfigureAwait(false);

            return StatusCode(StatusCodes.Status201Created);
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await loginUseCase.ExecuteAsync(
                new LoginClientCommand(request.NetworkId, request.Password),
                cancellationToken).ConfigureAwait(false);

            return Ok(new LoginResponse
            {
                Token = result.Token,
                ExpiresAtUtc = result.ExpiresAtUtc
            });
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }
}


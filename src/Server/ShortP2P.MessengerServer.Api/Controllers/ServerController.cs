using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Api.Http;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Server;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Route($"{ApiRoutes.Prefix}/server")]
public sealed class ServerController(
    GetServerCertificateUseCase certificateUseCase) : ControllerBase
{
    [HttpGet("certificate")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCertificateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var info = await certificateUseCase.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return Ok(info.ToDto());
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }
}

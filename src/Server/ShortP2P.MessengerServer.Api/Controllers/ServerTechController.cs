using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.ServerTech;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Route($"{ApiRoutes.Prefix}/server-tech")]
public sealed class ServerTechController(
    GetTotalPowerUseCase getTotalPowerUseCase,
    GetFreePowersUseCase getFreePowersUseCase) : ControllerBase
{
    [HttpGet("power")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPowerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (totalPower, measuredAtUtc) = await getTotalPowerUseCase
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return Ok(new ServerPowerResponse
            {
                TotalPower = totalPower,
                MeasuredAtUtc = measuredAtUtc
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Ok(new ServerPowerResponse
            {
                TotalPower = ServerHostPowers.DefaultTotalPower,
                MeasuredAtUtc = DateTime.UtcNow
            });
        }
    }

    [HttpGet("ping")]
    [AllowAnonymous]
    public IActionResult Ping() => Ok();

    [HttpGet("free-powers")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFreePowersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (freePowers, measuredAtUtc) = await getFreePowersUseCase
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return Ok(new ServerFreePowersResponse
            {
                FreePowers = freePowers,
                MeasuredAtUtc = measuredAtUtc
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Ok(new ServerFreePowersResponse
            {
                FreePowers = ServerHostPowers.DefaultFreePowers,
                MeasuredAtUtc = DateTime.UtcNow
            });
        }
    }
}

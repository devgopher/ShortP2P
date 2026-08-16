using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Api.Extensions;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Inbox;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Authorize]
[Route($"{ApiRoutes.Prefix}/events")]
public sealed class EventsController(PollInboxEventsUseCase pollInboxEventsUseCase) : ControllerBase
{
    [HttpGet("poll")]
    public async Task<IActionResult> PollAsync(
        [FromQuery] int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.RequireNetworkId();
            var deviceId = HttpContext.RequireDeviceId();
            var result = await pollInboxEventsUseCase
                .ExecuteAsync(new PollInboxEventsQuery(caller, deviceId, timeoutSeconds), cancellationToken)
                .ConfigureAwait(false);

            return Ok(new EventsPollResponse
            {
                Messages = result.Messages.Select(m => m.ToDto()).ToArray(),
                ChatRequests = result.ChatRequests.Select(r => r.ToDto()).ToArray()
            });
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }
}

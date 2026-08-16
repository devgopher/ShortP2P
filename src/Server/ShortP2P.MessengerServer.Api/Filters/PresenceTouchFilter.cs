using Microsoft.AspNetCore.Mvc.Filters;
using ShortP2P.MessengerServer.Api.Extensions;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Filters;

/// <summary>Upserts Online presence for the authenticated (networkId, deviceId) on every request.</summary>
public sealed class PresenceTouchFilter(IClientStatusRepository statuses, IClock clock) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            try
            {
                var networkId = context.HttpContext.RequireNetworkId();
                var deviceId = context.HttpContext.RequireDeviceId();
                await statuses.UpsertAsync(
                    new ClientStatuses
                    {
                        NetworkId = networkId,
                        DeviceId = deviceId,
                        Status = ClientOnlineStatus.Online,
                        CreatedAtUtc = clock.UtcNow
                    },
                    context.HttpContext.RequestAborted).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort: do not fail the primary request.
            }
        }

        await next().ConfigureAwait(false);
    }
}

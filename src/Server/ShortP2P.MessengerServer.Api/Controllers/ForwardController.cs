using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShortP2P.MessengerServer.Api.Extensions;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Forwards;
using DomainForwardKind = ShortP2P.MessengerServer.Domain.ForwardKind;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Authorize]
[Route(ApiRoutes.Forward)]
public sealed class ForwardController(
    ForwardPeerProfileUseCase forwardPeerProfileUseCase,
    ILogger<ForwardController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> ForwardAsync(
        [FromBody] ForwardRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request is null)
                throw UseCaseException.Validation("Request body is required.");

            var caller = HttpContext.RequireNetworkId();
            if (!string.Equals(request.SrcNetworkId?.Trim(), caller, StringComparison.Ordinal))
                throw UseCaseException.Unauthorized("srcNetworkId must match the authenticated client.");

            var serverOrigin = Request.Host.HasValue
                ? Request.Host.Value
                : Environment.MachineName;

            logger.LogDebug(
                "POST forward from {Remote} server={Server} body forwardId={ForwardId} src={Src} tgt={Tgt} kind={Kind} payloadBase64={Payload}",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?",
                serverOrigin,
                request.ForwardId,
                request.SrcNetworkId,
                request.TgtNetworkId,
                request.Kind,
                request.PayloadBase64);

            await forwardPeerProfileUseCase
                .ExecuteAsync(
                    new ForwardPeerProfileCommand(
                        request.ForwardId ?? "",
                        request.SrcNetworkId ?? "",
                        request.TgtNetworkId ?? "",
                        ToDomain(request.Kind),
                        request.PayloadBase64 ?? "",
                        serverOrigin),
                    cancellationToken)
                .ConfigureAwait(false);

            return Accepted();
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }

    private static DomainForwardKind ToDomain(ForwardKind kind) =>
        kind switch
        {
            ForwardKind.PeerProfileRequest => DomainForwardKind.PeerProfileRequest,
            ForwardKind.PeerProfileReply => DomainForwardKind.PeerProfileReply,
            _ => throw UseCaseException.Validation("Unsupported forward kind.")
        };
}

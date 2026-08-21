using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Api.Extensions;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Blobs;

namespace ShortP2P.MessengerServer.Api.Controllers;

[ApiController]
[Authorize]
[Route($"{ApiRoutes.Prefix}/blobs")]
public sealed class BlobsController(PutBlobUseCase putBlobUseCase, GetBlobUseCase getBlobUseCase) : ControllerBase
{
    [HttpPut("{blobId}")]
    [Consumes("application/octet-stream")]
    [RequestSizeLimit(BlobLimits.MaxCiphertextBytes)]
    public async Task<IActionResult> PutBlobAsync(
        string blobId,
        [FromQuery] string? targetNetworkId,
        [FromHeader(Name = BlobLimits.TargetNetworkIdHeader)] string? targetNetworkIdHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.RequireNetworkId();
            var tgt = !string.IsNullOrWhiteSpace(targetNetworkId)
                ? targetNetworkId.Trim()
                : targetNetworkIdHeader?.Trim() ?? "";

            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var ciphertext = buffer.ToArray();

            await putBlobUseCase
                .ExecuteAsync(new PutBlobCommand(blobId, caller, tgt, ciphertext), cancellationToken)
                .ConfigureAwait(false);

            return Accepted();
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }

    [HttpGet("{blobId}")]
    public async Task<IActionResult> GetBlobAsync(string blobId, CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.RequireNetworkId();
            var blob = await getBlobUseCase
                .ExecuteAsync(new GetBlobQuery(blobId, caller), cancellationToken)
                .ConfigureAwait(false);

            return File(blob.Ciphertext, "application/octet-stream");
        }
        catch (UseCaseException ex)
        {
            return this.ToApiErrorResult(ex);
        }
    }
}

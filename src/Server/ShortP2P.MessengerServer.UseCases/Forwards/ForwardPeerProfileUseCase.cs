using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Forwards;

public sealed record ForwardPeerProfileCommand(
    string ForwardId,
    string SrcNetworkId,
    string TgtNetworkId,
    ForwardKind Kind,
    string PayloadBase64,
    string ServerOrigin);

public sealed class ForwardPeerProfileUseCase(
    IForwardHub forwardHub,
    IClientStatusRepository statuses,
    IInboxWaitService inboxWait,
    IClock clock,
    IOptions<MessengerInboxOptions> inboxOptions,
    ILogger<ForwardPeerProfileUseCase> logger)
{
    public Task ExecuteAsync(ForwardPeerProfileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var forwardId = command.ForwardId?.Trim() ?? "";
        var src = command.SrcNetworkId?.Trim() ?? "";
        var tgt = command.TgtNetworkId?.Trim() ?? "";
        var payloadB64 = command.PayloadBase64?.Trim() ?? "";
        var serverOrigin = string.IsNullOrWhiteSpace(command.ServerOrigin)
            ? "?"
            : command.ServerOrigin.Trim();

        if (forwardId.Length == 0)
            throw UseCaseException.Validation("forwardId is required.");
        if (forwardId.Length > 128)
            throw UseCaseException.Validation("forwardId must be at most 128 characters.");
        if (src.Length == 0)
            throw UseCaseException.Validation("srcNetworkId is required.");
        if (tgt.Length == 0)
            throw UseCaseException.Validation("tgtNetworkId is required.");
        if (string.Equals(src, tgt, StringComparison.Ordinal))
            throw UseCaseException.Validation("srcNetworkId and tgtNetworkId must differ.");
        if (payloadB64.Length == 0)
            throw UseCaseException.Validation("payloadBase64 is required.");

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(payloadB64);
        }
        catch (FormatException)
        {
            throw UseCaseException.Validation("payloadBase64 is not valid base64.");
        }

        if (payload.Length == 0)
            throw UseCaseException.Validation("Decoded payload is empty.");

        var hashes = PeerProfileForwardPayloadInspector.ValidateAndHash(command.Kind, payload);

        logger.LogDebug(
            "Forward request body server={Server} forwardId={ForwardId} src={Src} tgt={Tgt} kind={Kind} payloadBase64={Payload}",
            serverOrigin, forwardId, src, tgt, command.Kind, payloadB64);

        return AcceptAsync(forwardId, src, tgt, command.Kind, payloadB64, hashes, serverOrigin, cancellationToken);
    }

    private async Task AcceptAsync(
        string forwardId,
        string src,
        string tgt,
        ForwardKind kind,
        string payloadB64,
        PeerProfileForwardPayloadInspector.InspectResult hashes,
        string serverOrigin,
        CancellationToken cancellationToken)
    {
        if (!await IsTargetOnlineAsync(tgt, cancellationToken).ConfigureAwait(false))
            throw UseCaseException.PeerOffline("Target peer is offline; forward was not queued.");

        var now = clock.UtcNow;
        forwardHub.PurgeExpired(now);

        var envelope = new ForwardEnvelope(forwardId, src, tgt, kind, payloadB64, now);
        if (!forwardHub.TryAdd(envelope))
        {
            logger.LogInformation(
                "Forward duplicate ignored src={Src} tgt={Tgt} forwardId={ForwardId} server={Server} avatarHash={AvatarHash} aboutHash={AboutHash}",
                src, tgt, forwardId, serverOrigin, hashes.AvatarHashHex, hashes.AboutHashHex);
            return;
        }

        logger.LogInformation(
            "Forward accepted src={Src} tgt={Tgt} forwardId={ForwardId} server={Server} avatarHash={AvatarHash} aboutHash={AboutHash}",
            src, tgt, forwardId, serverOrigin, hashes.AvatarHashHex, hashes.AboutHashHex);

        inboxWait.Notify(tgt);
    }

    private async Task<bool> IsTargetOnlineAsync(string tgtNetworkId, CancellationToken cancellationToken)
    {
        var onlineTimeout = inboxOptions.Value.OnlineTimeout;
        var now = clock.UtcNow;
        var all = await statuses.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Any(s =>
            string.Equals(s.NetworkId, tgtNetworkId, StringComparison.Ordinal) &&
            s.Status == ClientOnlineStatus.Online &&
            now - s.CreatedAtUtc <= onlineTimeout);
    }
}

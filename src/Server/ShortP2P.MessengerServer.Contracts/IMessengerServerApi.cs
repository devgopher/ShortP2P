using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.Contracts;

/// <summary>
/// Messenger server HTTPS operations.
/// Client implementation: <c>ShortP2P.MessengerServer.Http.MessengerServerApiClient</c>.
/// </summary>
public interface IMessengerServerApi
{
    /// <summary>POST <see cref="ApiRoutes.Register"/> — register client (nick, networkId, password, deviceId).</summary>
    Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.Login"/> — authorize by networkId, password and deviceId.</summary>
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.ServerCertificate"/> — server TLS certificate fingerprint.</summary>
    Task<ServerCertificateResponse> GetServerCertificateAsync(CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.Chats"/> — chats for the given networkId.</summary>
    Task<IReadOnlyList<ChatDto>> GetChatsAsync(GetChatsRequest request, CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.ChatRequests"/> — request a new chat with target subscriber.</summary>
    Task CreateChatRequestAsync(ChatRequestCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.Messages"/> — send an encrypted message.</summary>
    Task SendMessageAsync(MessageDto message, CancellationToken cancellationToken = default);

    /// <summary>
    /// PUT <see cref="ApiRoutes.Blobs"/>/{blobId} — store an opaque encrypted attachment (same envelope as messages).
    /// </summary>
    Task PutBlobAsync(
        string blobId,
        string targetNetworkId,
        ReadOnlyMemory<byte> ciphertext,
        CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.Blobs"/>/{blobId} — download opaque ciphertext.</summary>
    Task<byte[]> GetBlobAsync(string blobId, CancellationToken cancellationToken = default);

    /// <summary>DELETE <see cref="ApiRoutes.Blobs"/>/{blobId} — remove blob after successful receipt.</summary>
    Task DeleteBlobAsync(string blobId, CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.MessageReceipts"/> — submit a delivery receipt (deletes this device's inbox copy).</summary>
    Task SubmitDeliveryReceiptAsync(DeliveryReceiptRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <see cref="ApiRoutes.MessageReceipts"/> — all delivery receipts for the current client's networkId.
    /// </summary>
    Task<IReadOnlyList<DeliveryReceiptDto>> GetDeliveryReceiptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <see cref="ApiRoutes.EventsPoll"/> — long-poll inbox (messages + chat requests) for this device.
    /// </summary>
    Task<EventsPollResponse> PollEventsAsync(
        int? timeoutSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.Clients"/> — registered clients with online/offline status.</summary>
    Task<IReadOnlyList<ClientPresenceDto>> GetClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.ServerTechPower"/> — host TotalPower (anonymous).</summary>
    Task<ServerPowerResponse> GetPowerAsync(CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.ServerTechFreePowers"/> — host FreePowers % (anonymous).</summary>
    Task<ServerFreePowersResponse> GetFreePowersAsync(CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.ServerTechPing"/> — liveness (anonymous, 200 OK).</summary>
    Task PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <see cref="ApiRoutes.TrustAskRating"/> — report a known server (added at 0.8 if new)
    /// and receive this host's ratings of all known peers.
    /// </summary>
    Task<IReadOnlyList<ServerRatingDto>> AskRatingAsync(
        string serverIp,
        int serverPort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <see cref="ApiRoutes.TrustAskServers"/> — peers with rating ≥ 0.3.
    /// </summary>
    Task<IReadOnlyList<ServerRatingDto>> AskServersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// POST <see cref="ApiRoutes.TrustClaim"/> — complain about another server (not this host).
    /// </summary>
    Task ClaimServerAsync(
        string serverIp,
        int serverPort,
        ServerClaimReason reason,
        CancellationToken cancellationToken = default);
}

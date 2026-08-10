using ShortP2P.MessengerServer.Contracts.Dtos;

namespace ShortP2P.MessengerServer.Contracts;

/// <summary>
/// Messenger server HTTPS operations.
/// Client implementation: <c>ShortP2P.MessengerServer.Http.MessengerServerApiClient</c>.
/// </summary>
public interface IMessengerServerApi
{
    /// <summary>POST <see cref="ApiRoutes.Register"/> — register client (nick, networkId, password).</summary>
    Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.Login"/> — authorize by networkId and password.</summary>
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.ServerCertificate"/> — server TLS certificate fingerprint.</summary>
    Task<ServerCertificateResponse> GetServerCertificateAsync(CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.Chats"/> — chats for the given networkId.</summary>
    Task<IReadOnlyList<ChatDto>> GetChatsAsync(GetChatsRequest request, CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.ChatRequests"/> — request a new chat with target subscriber.</summary>
    Task CreateChatRequestAsync(ChatRequestCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.ChatRequests"/> — pending chat requests for the current client.</summary>
    Task<IReadOnlyList<ChatRequestDto>> GetChatRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>GET <see cref="ApiRoutes.Messages"/> — messages addressed to the current client's networkId.</summary>
    Task<IReadOnlyList<MessageDto>> GetMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.Messages"/> — send an encrypted message.</summary>
    Task SendMessageAsync(MessageDto message, CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.MessageReceipts"/> — submit a delivery receipt.</summary>
    Task SubmitDeliveryReceiptAsync(DeliveryReceiptRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET <see cref="ApiRoutes.MessageReceipts"/> — all delivery receipts for the current client's networkId.
    /// </summary>
    Task<IReadOnlyList<DeliveryReceiptDto>> GetDeliveryReceiptsAsync(CancellationToken cancellationToken = default);

    /// <summary>POST <see cref="ApiRoutes.KeepAlive"/> — presence keep-alive.</summary>
    Task KeepAliveAsync(KeepAliveRequest request, CancellationToken cancellationToken = default);
}

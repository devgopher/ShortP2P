using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;

namespace ShortP2P.MessengerServer.Http;

/// <summary>HTTPS implementation of <see cref="IMessengerServerApi"/> for ShortP2P clients.</summary>
public sealed class MessengerServerApiClient(
    HttpClient httpClient,
    IMessengerServerSession session) : IMessengerServerApi
{
    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient
            .PostAsync(ApiRoutes.Register, MessengerServerJson.ToJsonContent(request), cancellationToken)
            .ConfigureAwait(false);

        await MessengerServerJson.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient
            .PostAsync(ApiRoutes.Login, MessengerServerJson.ToJsonContent(request), cancellationToken)
            .ConfigureAwait(false);

        var login = await MessengerServerJson
            .ReadJsonAsync<LoginResponse>(response, cancellationToken)
            .ConfigureAwait(false);

        session.SetToken(login.Token, login.ExpiresAtUtc);
        return login;
    }

    public async Task<ServerCertificateResponse> GetServerCertificateAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync(ApiRoutes.ServerCertificate, cancellationToken)
            .ConfigureAwait(false);

        return await MessengerServerJson
            .ReadJsonAsync<ServerCertificateResponse>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatDto>> GetChatsAsync(
        GetChatsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NetworkId);

        var url = $"{ApiRoutes.Chats}?networkId={Uri.EscapeDataString(request.NetworkId.Trim())}";
        using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        var list = await MessengerServerJson
            .ReadJsonAsync<ChatDto[]>(response, cancellationToken)
            .ConfigureAwait(false);

        return list;
    }

    public async Task CreateChatRequestAsync(
        ChatRequestCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient
            .PostAsync(ApiRoutes.ChatRequests, MessengerServerJson.ToJsonContent(request), cancellationToken)
            .ConfigureAwait(false);

        await MessengerServerJson.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatRequestDto>> GetChatRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync(ApiRoutes.ChatRequests, cancellationToken)
            .ConfigureAwait(false);

        return await MessengerServerJson
            .ReadJsonAsync<ChatRequestDto[]>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync(ApiRoutes.Messages, cancellationToken)
            .ConfigureAwait(false);

        return await MessengerServerJson
            .ReadJsonAsync<MessageDto[]>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SendMessageAsync(MessageDto message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var response = await httpClient
            .PostAsync(ApiRoutes.Messages, MessengerServerJson.ToJsonContent(message), cancellationToken)
            .ConfigureAwait(false);

        await MessengerServerJson.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task SubmitDeliveryReceiptAsync(
        DeliveryReceiptRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient
            .PostAsync(ApiRoutes.MessageReceipts, MessengerServerJson.ToJsonContent(request), cancellationToken)
            .ConfigureAwait(false);

        await MessengerServerJson.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeliveryReceiptDto>> GetDeliveryReceiptsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync(ApiRoutes.MessageReceipts, cancellationToken)
            .ConfigureAwait(false);

        return await MessengerServerJson
            .ReadJsonAsync<DeliveryReceiptDto[]>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task KeepAliveAsync(KeepAliveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient
            .PostAsync(ApiRoutes.KeepAlive, MessengerServerJson.ToJsonContent(request), cancellationToken)
            .ConfigureAwait(false);

        await MessengerServerJson.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Clears the stored JWT (logout / switch account).</summary>
    public void Logout() => session.Clear();
}

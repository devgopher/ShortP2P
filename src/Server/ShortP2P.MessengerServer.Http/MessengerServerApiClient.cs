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

    public async Task SendMessageAsync(MessageDto message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var response = await httpClient
            .PostAsync(ApiRoutes.Messages, MessengerServerJson.ToJsonContent(message), cancellationToken)
            .ConfigureAwait(false);

        await MessengerServerJson.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task PutBlobAsync(
        string blobId,
        string targetNetworkId,
        ReadOnlyMemory<byte> ciphertext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNetworkId);

        var url =
            $"{BlobLimits.BlobById(blobId)}?targetNetworkId={Uri.EscapeDataString(targetNetworkId.Trim())}";
        using var content = new ByteArrayContent(ciphertext.ToArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        request.Headers.TryAddWithoutValidation(BlobLimits.TargetNetworkIdHeader, targetNetworkId.Trim());

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await MessengerServerJson.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> GetBlobAsync(string blobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobId);

        using var request = new HttpRequestMessage(HttpMethod.Get, BlobLimits.BlobById(blobId));
        request.Headers.Accept.ParseAdd("application/octet-stream");
        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await MessengerServerJson.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteBlobAsync(string blobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobId);

        using var response = await httpClient
            .DeleteAsync(BlobLimits.BlobById(blobId), cancellationToken)
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

    public async Task<EventsPollResponse> PollEventsAsync(
        int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var url = ApiRoutes.EventsPoll;
        if (timeoutSeconds is > 0)
            url += $"?timeoutSeconds={timeoutSeconds.Value}";

        using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        return await MessengerServerJson
            .ReadJsonAsync<EventsPollResponse>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClientPresenceDto>> GetClientsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync(ApiRoutes.Clients, cancellationToken)
            .ConfigureAwait(false);

        return await MessengerServerJson
            .ReadJsonAsync<ClientPresenceDto[]>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ServerPowerResponse> GetPowerAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync(ApiRoutes.ServerTechPower, cancellationToken)
            .ConfigureAwait(false);

        return await MessengerServerJson
            .ReadJsonAsync<ServerPowerResponse>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ServerFreePowersResponse> GetFreePowersAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync(ApiRoutes.ServerTechFreePowers, cancellationToken)
            .ConfigureAwait(false);

        return await MessengerServerJson
            .ReadJsonAsync<ServerFreePowersResponse>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync(ApiRoutes.ServerTechPing, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Clears the stored JWT (logout / switch account).</summary>
    public void Logout() => session.Clear();
}

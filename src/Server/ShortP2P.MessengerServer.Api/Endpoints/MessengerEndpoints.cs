using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.Api.Http;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Auth;
using ShortP2P.MessengerServer.UseCases.Chats;
using ShortP2P.MessengerServer.UseCases.Messages;
using ShortP2P.MessengerServer.UseCases.Presence;
using ShortP2P.MessengerServer.UseCases.Server;

namespace ShortP2P.MessengerServer.Api.Endpoints;

public static class MessengerEndpoints
{
    public static IEndpointRouteBuilder MapMessengerApi(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.Register, RegisterAsync)
            .WithName("Register")
            .WithTags("Auth")
            .AllowAnonymous();

        app.MapPost(ApiRoutes.Login, LoginAsync)
            .WithName("Login")
            .WithTags("Auth")
            .AllowAnonymous();

        app.MapGet(ApiRoutes.ServerCertificate, GetCertificateAsync)
            .WithName("GetServerCertificate")
            .WithTags("Server")
            .AllowAnonymous();

        app.MapGet(ApiRoutes.Chats, GetChatsAsync)
            .WithName("GetChats")
            .WithTags("Chats")
            .RequireAuthorization();

        app.MapPost(ApiRoutes.ChatRequests, CreateChatRequestAsync)
            .WithName("CreateChatRequest")
            .WithTags("Chats")
            .RequireAuthorization();

        app.MapGet(ApiRoutes.ChatRequests, GetChatRequestsAsync)
            .WithName("GetChatRequests")
            .WithTags("Chats")
            .RequireAuthorization();

        app.MapGet(ApiRoutes.Messages, GetMessagesAsync)
            .WithName("GetMessages")
            .WithTags("Messages")
            .RequireAuthorization();

        app.MapPost(ApiRoutes.Messages, SendMessageAsync)
            .WithName("SendMessage")
            .WithTags("Messages")
            .RequireAuthorization();

        app.MapPost(ApiRoutes.MessageReceipts, SubmitReceiptAsync)
            .WithName("SubmitDeliveryReceipt")
            .WithTags("Messages")
            .RequireAuthorization();

        app.MapGet(ApiRoutes.MessageReceipts, GetReceiptsAsync)
            .WithName("GetDeliveryReceipts")
            .WithTags("Messages")
            .RequireAuthorization();

        app.MapPost(ApiRoutes.KeepAlive, KeepAliveAsync)
            .WithName("KeepAlive")
            .WithTags("Presence")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        RegisterClientUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            await useCase.ExecuteAsync(
                new RegisterClientCommand(request.Nick, request.NetworkId, request.Password),
                cancellationToken).ConfigureAwait(false);
            return Results.StatusCode(StatusCodes.Status201Created);
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        LoginClientUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCase.ExecuteAsync(
                new LoginClientCommand(request.NetworkId, request.Password),
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(new LoginResponse
            {
                Token = result.Token,
                ExpiresAtUtc = result.ExpiresAtUtc
            });
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> GetCertificateAsync(
        GetServerCertificateUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = await useCase.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(info.ToDto());
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> GetChatsAsync(
        HttpContext http,
        string? networkId,
        GetChatsUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = http.RequireNetworkId();
            var queryNetworkId = string.IsNullOrWhiteSpace(networkId) ? caller : networkId.Trim();
            if (!string.Equals(queryNetworkId, caller, StringComparison.Ordinal))
                throw UseCaseException.Unauthorized("networkId must match the authenticated client.");

            var chats = await useCase.ExecuteAsync(new GetChatsQuery(caller), cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(chats.Select(c => c.ToDto()).ToArray());
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> CreateChatRequestAsync(
        HttpContext http,
        ChatRequestCreateRequest request,
        CreateChatRequestUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = http.RequireNetworkId();
            await useCase.ExecuteAsync(
                new CreateChatRequestCommand(caller, request.PublicKey, request.TargetNetworkId),
                cancellationToken).ConfigureAwait(false);
            return Results.Accepted();
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> GetChatRequestsAsync(
        HttpContext http,
        GetChatRequestsUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = http.RequireNetworkId();
            var list = await useCase.ExecuteAsync(new GetChatRequestsQuery(caller), cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(list.Select(x => x.ToDto()).ToArray());
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> GetMessagesAsync(
        HttpContext http,
        GetMessagesUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = http.RequireNetworkId();
            var list = await useCase.ExecuteAsync(new GetMessagesQuery(caller), cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(list.Select(x => x.ToDto()).ToArray());
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> SendMessageAsync(
        HttpContext http,
        MessageDto message,
        SendMessageUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = http.RequireNetworkId();
            if (!string.Equals(message.SrcNetworkId, caller, StringComparison.Ordinal))
                throw UseCaseException.Unauthorized("srcNetworkId must match the authenticated client.");

            await useCase.ExecuteAsync(new SendMessageCommand(message.ToDomain()), cancellationToken)
                .ConfigureAwait(false);
            return Results.Accepted();
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> SubmitReceiptAsync(
        HttpContext http,
        DeliveryReceiptRequest request,
        SubmitDeliveryReceiptUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = http.RequireNetworkId();
            await useCase.ExecuteAsync(
                new SubmitDeliveryReceiptCommand(caller, request.MessageId, request.ReceivedAtUtc),
                cancellationToken).ConfigureAwait(false);
            return Results.Accepted();
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> GetReceiptsAsync(
        HttpContext http,
        GetDeliveryReceiptsUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = http.RequireNetworkId();
            var list = await useCase.ExecuteAsync(new GetDeliveryReceiptsQuery(caller), cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(list.Select(x => x.ToDto()).ToArray());
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }

    private static async Task<IResult> KeepAliveAsync(
        HttpContext http,
        KeepAliveRequest request,
        KeepAliveUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var caller = http.RequireNetworkId();
            if (!string.Equals(request.NetworkId?.Trim(), caller, StringComparison.Ordinal))
                throw UseCaseException.Unauthorized("networkId must match the authenticated client.");

            await useCase.ExecuteAsync(new KeepAliveCommand(caller), cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (UseCaseException ex)
        {
            return ApiResults.FromException(ex);
        }
    }
}

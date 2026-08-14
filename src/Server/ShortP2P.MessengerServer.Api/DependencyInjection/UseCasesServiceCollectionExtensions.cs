using ShortP2P.MessengerServer.UseCases.Auth;
using ShortP2P.MessengerServer.UseCases.Chats;
using ShortP2P.MessengerServer.UseCases.Messages;
using ShortP2P.MessengerServer.UseCases.Presence;
using ShortP2P.MessengerServer.UseCases.Server;

namespace ShortP2P.MessengerServer.Api.DependencyInjection;

public static class UseCasesServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerUseCases(this IServiceCollection services)
    {
        services.AddScoped<RegisterClientUseCase>();
        services.AddScoped<LoginClientUseCase>();
        services.AddScoped<GetChatsUseCase>();
        services.AddScoped<CreateChatRequestUseCase>();
        services.AddScoped<GetChatRequestsUseCase>();
        services.AddScoped<SendMessageUseCase>();
        services.AddScoped<GetMessagesUseCase>();
        services.AddScoped<SubmitDeliveryReceiptUseCase>();
        services.AddScoped<GetDeliveryReceiptsUseCase>();
        services.AddScoped<KeepAliveUseCase>();
        services.AddScoped<GetClientPresencesUseCase>();
        services.AddScoped<GetServerCertificateUseCase>();
        return services;
    }
}

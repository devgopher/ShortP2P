using ShortP2P.MessengerServer.Api.HostPowers;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.Auth;
using ShortP2P.MessengerServer.UseCases.Blobs;
using ShortP2P.MessengerServer.UseCases.Chats;
using ShortP2P.MessengerServer.UseCases.Hosting;
using ShortP2P.MessengerServer.UseCases.Inbox;
using ShortP2P.MessengerServer.UseCases.Messages;
using ShortP2P.MessengerServer.UseCases.Presence;
using ShortP2P.MessengerServer.UseCases.Server;
using ShortP2P.MessengerServer.UseCases.ServerTech;
using ShortP2P.MessengerServer.UseCases.Trust;

namespace ShortP2P.MessengerServer.Api.DependencyInjection;

public static class UseCasesServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerUseCases(this IServiceCollection services)
    {
        services.AddSingleton<IInboxWaitService, InboxWaitService>();
        services.AddSingleton<IHostHardwareInfoProvider, OsHostHardwareInfoProvider>();
        services.AddSingleton<IHostLoadInfoProvider, OsHostLoadInfoProvider>();
        services.AddScoped<HostPowersMeasurementService>();
        services.AddScoped<GetTotalPowerUseCase>();
        services.AddScoped<GetFreePowersUseCase>();
        services.AddScoped<DeviceFanoutService>();
        services.AddScoped<RegisterClientUseCase>();
        services.AddScoped<LoginClientUseCase>();
        services.AddScoped<GetChatsUseCase>();
        services.AddScoped<CreateChatRequestUseCase>();
        services.AddScoped<SendMessageUseCase>();
        services.AddScoped<SubmitDeliveryReceiptUseCase>();
        services.AddScoped<GetDeliveryReceiptsUseCase>();
        services.AddScoped<PutBlobUseCase>();
        services.AddScoped<GetBlobUseCase>();
        services.AddScoped<DeleteBlobUseCase>();
        services.AddScoped<PollInboxEventsUseCase>();
        services.AddScoped<GetClientPresencesUseCase>();
        services.AddScoped<GetServerCertificateUseCase>();
        services.AddScoped<AskRatingUseCase>();
        services.AddScoped<AskServersUseCase>();
        services.AddScoped<ClaimServerUseCase>();
        services.AddHostedService<MessageRetentionHostedService>();
        services.AddHostedService<TrustRecoveryHostedService>();
        services.AddHostedService<HostPowersMeasurementHostedService>();
        return services;
    }
}

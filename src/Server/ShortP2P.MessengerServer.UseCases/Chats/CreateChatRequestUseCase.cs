using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.Inbox;

namespace ShortP2P.MessengerServer.UseCases.Chats;

public sealed class CreateChatRequestUseCase(
    IChatRepository chats,
    IChatRequestRepository chatRequests,
    ICryptoKeysRepository cryptoKeys,
    DeviceFanoutService fanout,
    IInboxWaitService inboxWait,
    IClock clock)
{
    public async Task ExecuteAsync(CreateChatRequestCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.CallerNetworkId) ||
            string.IsNullOrWhiteSpace(command.PublicKey) ||
            string.IsNullOrWhiteSpace(command.TargetNetworkId))
        {
            throw UseCaseException.Validation("CallerNetworkId, publicKey and targetNetworkId are required.");
        }

        var caller = command.CallerNetworkId.Trim();
        var target = command.TargetNetworkId.Trim();
        if (string.Equals(caller, target, StringComparison.Ordinal))
            throw UseCaseException.Validation("Cannot create a chat with yourself.");

        var now = clock.UtcNow;

        var existing = await chats.FindByParticipantsAsync(caller, target, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await chats.AddAsync(
                new Chat
                {
                    ChatId = Guid.NewGuid().ToString("N"),
                    NetworkIds = [caller, target],
                    CreatedAtUtc = now
                },
                cancellationToken).ConfigureAwait(false);
        }

        var request = new ChatRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            RequesterNetworkId = caller,
            TargetNetworkId = target,
            PublicKey = command.PublicKey,
            CreatedAtUtc = now
        };

        await chatRequests.AddAsync(request, cancellationToken).ConfigureAwait(false);
        await fanout.FanOutChatRequestAsync(request, cancellationToken).ConfigureAwait(false);

        await cryptoKeys.UpsertAsync(
            new CryptoKeys
            {
                SrcNetworkId = caller,
                TgtNetworkId = target,
                PublicKey = command.PublicKey
            },
            cancellationToken).ConfigureAwait(false);

        inboxWait.Notify(target);
    }
}

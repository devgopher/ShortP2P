using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Messages;

public sealed class SendMessageUseCase(
    IMessageRepository messages,
    IMessageCache messageCache)
{
    public async Task ExecuteAsync(SendMessageCommand command, CancellationToken cancellationToken = default)
    {
        var message = command.Message ?? throw UseCaseException.Validation("Message is required.");

        if (string.IsNullOrWhiteSpace(message.MessageId) ||
            string.IsNullOrWhiteSpace(message.SrcNetworkId) ||
            string.IsNullOrWhiteSpace(message.TgtNetworkId) ||
            string.IsNullOrWhiteSpace(message.EncryptedDataBase64))
        {
            throw UseCaseException.Validation(
                "MessageId, srcNetworkId, tgtNetworkId and encryptedDataBase64 are required.");
        }

        var existingInCache = await messageCache.FindByIdAsync(message.MessageId, cancellationToken)
            .ConfigureAwait(false);
        if (existingInCache is not null)
            return;

        var existingInRepo = await messages.FindByIdAsync(message.MessageId, cancellationToken)
            .ConfigureAwait(false);
        if (existingInRepo is not null)
            return;

        await Task.WhenAll(
            messageCache.AddAsync(message, cancellationToken),
            messages.AddAsync(message, cancellationToken)).ConfigureAwait(false);
    }
}

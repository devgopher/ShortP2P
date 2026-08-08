using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Messages;

public sealed class SubmitDeliveryReceiptUseCase(
    IMessageRepository messages,
    IMessageCache messageCache,
    IDeliveryTicketRepository tickets,
    IDeliveryTicketCache ticketCache)
{
    public async Task ExecuteAsync(
        SubmitDeliveryReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.CallerNetworkId) || string.IsNullOrWhiteSpace(command.MessageId))
            throw UseCaseException.Validation("CallerNetworkId and messageId are required.");

        var caller = command.CallerNetworkId.Trim();
        var messageId = command.MessageId.Trim();

        var message = await messageCache.FindByIdAsync(messageId, cancellationToken).ConfigureAwait(false)
                      ?? await messages.FindByIdAsync(messageId, cancellationToken).ConfigureAwait(false);
        if (message is null)
            throw UseCaseException.NotFound("Message not found.");

        if (!string.Equals(message.TgtNetworkId, caller, StringComparison.Ordinal))
            throw UseCaseException.Unauthorized("Only the message recipient can submit a delivery receipt.");

        var ticket = new DeliveryTicket
        {
            MessageId = messageId,
            ReceivedAtUtc = DateTime.SpecifyKind(command.ReceivedAtUtc, DateTimeKind.Utc)
        };

        await Task.WhenAll(
            ticketCache.AddAsync(new CachedDeliveryTicket(ticket, message.SrcNetworkId), cancellationToken),
            tickets.AddAsync(ticket, cancellationToken)).ConfigureAwait(false);
    }
}

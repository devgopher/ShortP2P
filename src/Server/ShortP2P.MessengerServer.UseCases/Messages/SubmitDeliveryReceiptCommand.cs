namespace ShortP2P.MessengerServer.UseCases.Messages;

public sealed record SubmitDeliveryReceiptCommand(
    string CallerNetworkId,
    string MessageId,
    DateTime ReceivedAtUtc);
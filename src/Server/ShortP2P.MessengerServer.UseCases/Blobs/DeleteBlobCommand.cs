namespace ShortP2P.MessengerServer.UseCases.Blobs;

public sealed record DeleteBlobCommand(string BlobId, string CallerNetworkId);

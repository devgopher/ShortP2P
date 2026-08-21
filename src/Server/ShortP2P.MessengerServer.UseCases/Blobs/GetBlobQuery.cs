namespace ShortP2P.MessengerServer.UseCases.Blobs;

public sealed record GetBlobQuery(string BlobId, string CallerNetworkId);

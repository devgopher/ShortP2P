namespace ShortP2P.MessengerServer.UseCases.Blobs;

public sealed record PutBlobCommand(
    string BlobId,
    string SrcNetworkId,
    string TgtNetworkId,
    byte[] Ciphertext);

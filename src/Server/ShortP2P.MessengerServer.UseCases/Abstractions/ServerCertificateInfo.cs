namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public sealed record ServerCertificateInfo(
    string FingerprintSha256,
    string? Subject,
    DateTime? NotAfterUtc);
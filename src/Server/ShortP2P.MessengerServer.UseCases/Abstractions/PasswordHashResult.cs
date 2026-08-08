namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Password hashing result (salt + hash, typically base64).</summary>
public sealed record PasswordHashResult(string Salt, string Hash);

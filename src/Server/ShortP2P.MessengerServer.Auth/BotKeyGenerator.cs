using System.Security.Cryptography;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Auth;

/// <summary>
///     Cryptographic bot-key generator. Output is base64
///     (matches <c>BotLimits.BotKeyLength</c> in Contracts).
/// </summary>
public sealed class BotKeyGenerator : IBotKeyGenerator
{
    public string Generate(int length = 64)
    {
        if (length < 1)
            throw new ArgumentOutOfRangeException(nameof(length), "Must be positive.");

        var keyByteLength = length * 3 / 4;
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(keyByteLength));

        return key.Length != length
            ? throw new InvalidOperationException($"Bot key length must be {length}, got {key.Length}.")
            : key;
    }
}
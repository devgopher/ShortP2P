namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>
/// Generates a new bot authentication key for a specific server.
/// The key is secret: shared only between that server and the bot.
/// </summary>
public interface IBotKeyGenerator
{
    /// <summary>
    /// Returns a new random bot key (base64).
    /// </summary>
    string Generate(int length = 64);
}

using ShortP2P.Client.Services.MessengerServers;
using ShortP2P.Crypto;

namespace ShortP2P.Messenger.Tests;

public class MessengerServerPayloadCodecTests
{
    [Fact]
    public void EncryptDecrypt_BinaryEnvelope_Roundtrip()
    {
        var keys = P2PCrypto.GenerateKeyPair();
        var plaintext = "S2P1-attachment"u8.ToArray();

        var envelope = MessengerServerPayloadCodec.Encrypt(plaintext, keys.PublicKey);
        Assert.Equal(plaintext, MessengerServerPayloadCodec.Decrypt(envelope, keys.PrivateKey));

        var asBase64 = MessengerServerPayloadCodec.EncryptToBase64(plaintext, keys.PublicKey);
        Assert.Equal(plaintext, MessengerServerPayloadCodec.DecryptFromBase64(asBase64, keys.PrivateKey));
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using ShortP2P.Crypto;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Messenger.Tests;

public class MessengerServiceNackTests
{
    [Fact]
    public async Task SendBinaryAsyncExpectAck_WhenChunksAreLost_ShouldRecoverViaNackAndAck()
    {
        var bobKeys = P2PCrypto.GenerateKeyPair();
        var hs = P2PCrypto.CreateHandshakeInitiation(bobKeys.PublicKey);
        var aliceSession = hs.Session;
        var bobSession = P2PCrypto.CreateSession(bobKeys.PrivateKey, hs.HandshakePacket);

        var aliceAddress = new TransportAddress(TransportKind.Bluetooth, [1, 2, 3, 4, 5, 6]);
        var bobAddress = new TransportAddress(TransportKind.Bluetooth, [7, 8, 9, 10, 11, 12]);

        var dropInitialChunkIndices = new HashSet<int> { 2, 6 };
        var initialChunkDropCounter = 0;
        var resentChunkCounter = 0;

        var aliceToBobDeliveriesPerChunk = new ConcurrentDictionary<int, int>();
        var bobReceivedPayload = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        MessengerService? alice = null;
        MessengerService? bob = null;

        async ValueTask AliceToBob(ReadOnlyMemory<byte> payload, TransportAddress destination, CancellationToken ct)
        {
            Assert.Equal(bobAddress.Data, destination.Data);
            var plain = bobSession.Decrypt(payload.ToArray());
            if (ChunkCodecTryParse(plain, out _, out var chunkIndex, out _, out _))
            {
                var sendCount = aliceToBobDeliveriesPerChunk.AddOrUpdate(chunkIndex, 1, (_, old) => old + 1);
                var isInitialSend = sendCount == 1;
                if (isInitialSend && dropInitialChunkIndices.Contains(chunkIndex))
                {
                    Interlocked.Increment(ref initialChunkDropCounter);
                    return;
                }

                if (!isInitialSend && dropInitialChunkIndices.Contains(chunkIndex))
                    Interlocked.Increment(ref resentChunkCounter);
            }

            var accepted = bob!.TryAcceptCipher(new TransportReceiveMessage(payload, aliceAddress));
            Assert.True(accepted);
            await ValueTask.CompletedTask;
        }

        async ValueTask BobToAlice(ReadOnlyMemory<byte> payload, TransportAddress destination, CancellationToken ct)
        {
            Assert.Equal(aliceAddress.Data, destination.Data);
            var accepted = alice!.TryAcceptCipher(new TransportReceiveMessage(payload, bobAddress));
            Assert.True(accepted);
            await ValueTask.CompletedTask;
        }

        var options = new MessengerOptions
        {
            ReassemblyTimeout = TimeSpan.FromMilliseconds(40),
            OutboundChunkCacheTtl = TimeSpan.FromSeconds(5),
            MaxNackChunkIndices = 128,
            MaxBinaryMessageBytes = 1024 * 1024
        };

        alice = new MessengerService(
            AliceToBob,
            _ => ValueTask.FromResult(aliceSession),
            options);

        bob = new MessengerService(
            BobToAlice,
            _ => ValueTask.FromResult(bobSession),
            options);

        bob.GotData += (_, incoming) => { bobReceivedPayload.TrySetResult(incoming.Payload.ToArray()); };

        await alice.StartAsync();
        await bob.StartAsync();

        try
        {
            var message = RandomBytes(10 * 1024 + 123);
            await alice.SendBinaryAsyncExpectAck(message, [bobAddress], TimeSpan.FromSeconds(3));

            var received = await bobReceivedPayload.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(message, received);

            Assert.Equal(dropInitialChunkIndices.Count, initialChunkDropCounter);
            Assert.Equal(dropInitialChunkIndices.Count, resentChunkCounter);

            foreach (var dropped in dropInitialChunkIndices)
            {
                Assert.True(aliceToBobDeliveriesPerChunk.TryGetValue(dropped, out var count));
                Assert.True(count >= 2, $"Dropped chunk {dropped} was not resent.");
            }
        }
        finally
        {
            await alice.StopAsync();
            await bob.StopAsync();
            await alice.DisposeAsync();
            await bob.DisposeAsync();
        }
    }

    private static bool ChunkCodecTryParse(byte[] plaintext, out Guid messageId, out int chunkIndex,
        out int totalChunks,
        out ReadOnlySpan<byte> payload)
    {
        const int headerBytes = 24;
        if (plaintext.Length < headerBytes)
        {
            messageId = Guid.Empty;
            chunkIndex = 0;
            totalChunks = 0;
            payload = default;
            return false;
        }

        messageId = new Guid(plaintext.AsSpan(0, 16));
        chunkIndex = (plaintext[16] << 24) | (plaintext[17] << 16) | (plaintext[18] << 8) | plaintext[19];
        totalChunks = (plaintext[20] << 24) | (plaintext[21] << 16) | (plaintext[22] << 8) | plaintext[23];
        payload = plaintext.AsSpan(headerBytes);

        return totalChunks > 0 && chunkIndex >= 0 && chunkIndex < totalChunks;
    }

    private static byte[] RandomBytes(int len)
    {
        var data = new byte[len];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);
        return data;
    }
}
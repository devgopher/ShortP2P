using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Transport;

namespace ShortP2P.Messenger.Tests;

public class TransferWireAndTcpTests
{
    [Fact]
    public void ChatWireCodec_TransferOffer_Roundtrip()
    {
        var offer = new ChatWireTransferOffer(
            "tr-1",
            "tok-1",
            "document",
            "file.pdf",
            "application/pdf",
            1234,
            "127.0.0.1",
            9000,
            DateTimeOffset.UtcNow.AddMinutes(1).UtcTicks);

        var wire = ChatWireCodec.EncodeTransferOffer(offer);
        var ok = ChatWireCodec.TryParse(wire, out var parsed);

        Assert.True(ok);
        var got = Assert.IsType<ChatWireTransferOffer>(parsed);
        Assert.Equal(offer.TransferId, got.TransferId);
        Assert.Equal(offer.TransferToken, got.TransferToken);
        Assert.Equal(offer.PayloadKind, got.PayloadKind);
        Assert.Equal(offer.FileName, got.FileName);
        Assert.Null(got.BlobId);

        var withBlob = offer with { BlobId = "tr-1" };
        var wire2 = ChatWireCodec.EncodeTransferOffer(withBlob);
        Assert.True(ChatWireCodec.TryParse(wire2, out var parsed2));
        var got2 = Assert.IsType<ChatWireTransferOffer>(parsed2);
        Assert.Equal("tr-1", got2.ResolveBlobId());
    }

    [Fact]
    public async Task TcpTransferService_SendAndReceive_Roundtrip()
    {
        var transfer = new TcpTransferService();
        var payload = "hello-binary"u8.ToArray();
        var lease = await transfer.CreateListenerAsync("tr-2", "tok-2", TimeSpan.FromSeconds(10), default);
        try
        {
            var recvTask = transfer.AcceptAndReceiveAsync(lease, payload.Length, default);
            await transfer.SendAsync("127.0.0.1", lease.Port, "tr-2", "tok-2", payload, default);
            var got = await recvTask;
            Assert.Equal(payload, got);
        }
        finally
        {
            lease.Dispose();
        }
    }
}
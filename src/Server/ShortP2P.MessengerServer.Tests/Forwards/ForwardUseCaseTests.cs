using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Forwards;

namespace ShortP2P.MessengerServer.Tests.Forwards;

public class ForwardPeerProfileUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WhenTargetOnline_QueuesForward()
    {
        var h = new TestHarness();
        await h.Stores.StatusesRepo.UpsertAsync(new ClientStatuses
        {
            NetworkId = "bob",
            DeviceId = TestIds.DeviceB,
            Status = ClientOnlineStatus.Online,
            CreatedAtUtc = TestHarness.T0
        });

        var payload = ForwardTestPayloads.BuildRequestPayload();
        await h.ForwardPeerProfile().ExecuteAsync(new ForwardPeerProfileCommand(
            "fwd-1",
            "alice",
            "bob",
            ForwardKind.PeerProfileRequest,
            Convert.ToBase64String(payload),
            "server-a"));

        var taken = h.ForwardHub.TakeForTarget("bob", TestHarness.T0);
        Assert.Single(taken);
        Assert.Equal("fwd-1", taken[0].ForwardId);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetOffline_ThrowsPeerOffline()
    {
        var h = new TestHarness();
        var payload = ForwardTestPayloads.BuildRequestPayload();

        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.ForwardPeerProfile().ExecuteAsync(new ForwardPeerProfileCommand(
                "fwd-1",
                "alice",
                "bob",
                ForwardKind.PeerProfileRequest,
                Convert.ToBase64String(payload),
                "server-a")));
        Assert.Equal("PeerOffline", ex.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDuplicateForwardId_IsIgnored()
    {
        var h = new TestHarness();
        await h.Stores.StatusesRepo.UpsertAsync(new ClientStatuses
        {
            NetworkId = "bob",
            DeviceId = TestIds.DeviceB,
            Status = ClientOnlineStatus.Online,
            CreatedAtUtc = TestHarness.T0
        });

        var payloadB64 = Convert.ToBase64String(ForwardTestPayloads.BuildRequestPayload());
        var cmd = new ForwardPeerProfileCommand(
            "fwd-1", "alice", "bob", ForwardKind.PeerProfileRequest, payloadB64, "s");

        await h.ForwardPeerProfile().ExecuteAsync(cmd);
        await h.ForwardPeerProfile().ExecuteAsync(cmd);

        Assert.Single(h.ForwardHub.TakeForTarget("bob", TestHarness.T0));
    }

    [Fact]
    public async Task ExecuteAsync_WhenSelfTarget_ThrowsValidation()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.ForwardPeerProfile().ExecuteAsync(new ForwardPeerProfileCommand(
                "fwd-1",
                "alice",
                "alice",
                ForwardKind.PeerProfileRequest,
                Convert.ToBase64String(ForwardTestPayloads.BuildRequestPayload()),
                "s")));
        Assert.Equal("Validation", ex.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvalidBase64_ThrowsValidation()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.ForwardPeerProfile().ExecuteAsync(new ForwardPeerProfileCommand(
                "fwd-1", "alice", "bob", ForwardKind.PeerProfileRequest, "%%%", "s")));
        Assert.Equal("Validation", ex.Code);
    }
}

public class PeerProfileForwardPayloadInspectorTests
{
    [Fact]
    public void ValidateAndHash_Request_AcceptsExactFrame()
    {
        var result = PeerProfileForwardPayloadInspector.ValidateAndHash(
            ForwardKind.PeerProfileRequest,
            ForwardTestPayloads.BuildRequestPayload());
        Assert.Equal("", result.AvatarHashHex);
        Assert.Equal("", result.AboutHashHex);
    }

    [Fact]
    public void ValidateAndHash_Request_RejectsWrongLength()
    {
        var ex = Assert.Throws<UseCaseException>(() =>
            PeerProfileForwardPayloadInspector.ValidateAndHash(
                ForwardKind.PeerProfileRequest,
                new byte[] { 0x44 }));
        Assert.Equal("Validation", ex.Code);
    }

    [Fact]
    public void ValidateAndHash_Reply_ReturnsHashes()
    {
        var about = Encoding.UTF8.GetBytes("hello");
        var avatar = new byte[] { 1, 2, 3 };
        var payload = ForwardTestPayloads.BuildReplyPayload(about, avatar);

        var result = PeerProfileForwardPayloadInspector.ValidateAndHash(
            ForwardKind.PeerProfileReply,
            payload);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(avatar)), result.AvatarHashHex);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(about)), result.AboutHashHex);
    }

    [Fact]
    public void ValidateAndHash_Reply_RejectsAboutTooLarge()
    {
        var about = new byte[ForwardPayloadLimits.MaxAboutMeUtf8Bytes + 1];
        var payload = ForwardTestPayloads.BuildReplyPayload(about, []);

        var ex = Assert.Throws<UseCaseException>(() =>
            PeerProfileForwardPayloadInspector.ValidateAndHash(ForwardKind.PeerProfileReply, payload));
        Assert.Equal("PayloadTooLarge", ex.Code);
    }
}

internal static class ForwardTestPayloads
{
    public static byte[] BuildRequestPayload()
    {
        var payload = new byte[ForwardPayloadLimits.RequestLength];
        payload[0] = ForwardPayloadLimits.FrameRequest;
        return payload;
    }

    public static byte[] BuildReplyPayload(byte[] about, byte[] avatar)
    {
        var len = 1 + 8 + ForwardPayloadLimits.NetworkIdWireLength + 2 + about.Length + 2 + avatar.Length;
        var payload = new byte[len];
        payload[0] = ForwardPayloadLimits.FrameReply;
        var aboutOff = 1 + 8 + ForwardPayloadLimits.NetworkIdWireLength;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(aboutOff, 2), (ushort)about.Length);
        about.CopyTo(payload, aboutOff + 2);
        var avatarOff = aboutOff + 2 + about.Length;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(avatarOff, 2), (ushort)avatar.Length);
        avatar.CopyTo(payload, avatarOff + 2);
        return payload;
    }
}

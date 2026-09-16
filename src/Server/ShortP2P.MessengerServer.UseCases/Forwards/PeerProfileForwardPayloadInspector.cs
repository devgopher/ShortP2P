using System.Buffers.Binary;
using System.Security.Cryptography;
using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Forwards;

/// <summary>
/// Light inspection of 0x44/0x45 frames for validation and logging hashes.
/// Does not keep profile content beyond the caller's buffer.
/// </summary>
internal static class PeerProfileForwardPayloadInspector
{
    public readonly record struct InspectResult(string AvatarHashHex, string AboutHashHex);

    public static InspectResult ValidateAndHash(ForwardKind kind, ReadOnlySpan<byte> payload)
    {
        return kind switch
        {
            ForwardKind.PeerProfileRequest => ValidateRequest(payload),
            ForwardKind.PeerProfileReply => ValidateReply(payload),
            _ => throw UseCaseException.Validation("Unsupported forward kind.")
        };
    }

    private static InspectResult ValidateRequest(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != ForwardPayloadLimits.RequestLength)
            throw UseCaseException.Validation("PeerProfileRequest payload length is invalid.");
        if (payload[0] != ForwardPayloadLimits.FrameRequest)
            throw UseCaseException.Validation("PeerProfileRequest payload must start with 0x44.");
        return new InspectResult("", "");
    }

    private static InspectResult ValidateReply(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1 + 8 + ForwardPayloadLimits.NetworkIdWireLength + 2 + 2)
            throw UseCaseException.Validation("PeerProfileReply payload is too short.");
        if (payload.Length > ForwardPayloadLimits.MaxReplyLength)
        {
            throw UseCaseException.PayloadTooLarge(
                $"PeerProfileReply payload exceeds {ForwardPayloadLimits.MaxReplyLength} bytes.");
        }

        if (payload[0] != ForwardPayloadLimits.FrameReply)
            throw UseCaseException.Validation("PeerProfileReply payload must start with 0x45.");

        var aboutLenOff = 1 + 8 + ForwardPayloadLimits.NetworkIdWireLength;
        var aboutLen = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(aboutLenOff, 2));
        if (aboutLen > ForwardPayloadLimits.MaxAboutMeUtf8Bytes)
        {
            throw UseCaseException.PayloadTooLarge(
                $"AboutMe section exceeds {ForwardPayloadLimits.MaxAboutMeUtf8Bytes} UTF-8 bytes.");
        }

        if (payload.Length < aboutLenOff + 2 + aboutLen + 2)
            throw UseCaseException.Validation("PeerProfileReply AboutMe length is inconsistent.");

        var aboutSpan = payload.Slice(aboutLenOff + 2, aboutLen);
        var avatarLenOff = aboutLenOff + 2 + aboutLen;
        var avatarLen = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(avatarLenOff, 2));
        if (avatarLen > ForwardPayloadLimits.MaxAvatarBytes)
        {
            throw UseCaseException.PayloadTooLarge(
                $"Avatar exceeds {ForwardPayloadLimits.MaxAvatarBytes} bytes.");
        }

        if (payload.Length < avatarLenOff + 2 + avatarLen)
            throw UseCaseException.Validation("PeerProfileReply Avatar length is inconsistent.");
        if (payload.Length != avatarLenOff + 2 + avatarLen)
            throw UseCaseException.Validation("PeerProfileReply has trailing bytes.");

        var avatarSpan = payload.Slice(avatarLenOff + 2, avatarLen);
        return new InspectResult(ToSha256Hex(avatarSpan), ToSha256Hex(aboutSpan));
    }

    private static string ToSha256Hex(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return "";

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash);
    }
}

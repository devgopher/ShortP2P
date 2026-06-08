using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace ShortP2P.Client.ChatMedia;

/// <summary>
///     Бинарный формат полезной нагрузки внутри шифрованного messenger-кадра (совместимость со старыми клиентами: без
///     префикса = UTF-8 текст).
/// </summary>
public static class ChatWireCodec
{
    private const byte KindText = 0x01;
    private const byte KindImage = 0x02;
    private const byte KindFile = 0x03;
    private const byte KindTransferOffer = 0x04;
    private const byte KindTransferControl = 0x05;
    private static ReadOnlySpan<byte> Magic => "S2P1"u8;

    /// <summary>Магия S2P1 без успешного разбора вида — чтобы не показывать мусор как текст UTF-8.</summary>
    public static bool LooksLikeFramedWire(ReadOnlySpan<byte> payload)
    {
        return payload.Length >= Magic.Length && payload.StartsWith(Magic);
    }

    public static byte[] EncodeText(string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var buf = new byte[Magic.Length + 1 + 4 + utf8.Length];
        Magic.CopyTo(buf);
        buf[Magic.Length] = KindText;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(Magic.Length + 1, 4), (uint)utf8.Length);
        utf8.CopyTo(buf.AsSpan(Magic.Length + 1 + 4));
        return buf;
    }

    public static byte[] EncodeImage(string mimeType, ReadOnlySpan<byte> imageBytes)
    {
        var mime = Encoding.UTF8.GetBytes(mimeType.Trim());
        if (mime.Length > ushort.MaxValue)
            throw new ArgumentException("MIME type is too long.", nameof(mimeType));

        var buf = new byte[Magic.Length + 1 + 2 + mime.Length + 4 + imageBytes.Length];
        Magic.CopyTo(buf);
        buf[Magic.Length] = KindImage;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(Magic.Length + 1, 2), (ushort)mime.Length);
        mime.CopyTo(buf.AsSpan(Magic.Length + 1 + 2));
        var o = Magic.Length + 1 + 2 + mime.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o, 4), (uint)imageBytes.Length);
        imageBytes.CopyTo(buf.AsSpan(o + 4));
        return buf;
    }

    public static byte[] EncodeFile(string fileName, string mimeType, ReadOnlySpan<byte> fileBytes)
    {
        var mime = Encoding.UTF8.GetBytes(mimeType.Trim());
        if (mime.Length > ushort.MaxValue)
            throw new ArgumentException("MIME type is too long.", nameof(mimeType));

        var name = Encoding.UTF8.GetBytes(NormalizeWireFileName(fileName));
        if (name.Length > ushort.MaxValue)
            throw new ArgumentException("File name is too long.", nameof(fileName));

        var buf = new byte[Magic.Length + 1 + 2 + mime.Length + 2 + name.Length + 4 + fileBytes.Length];
        Magic.CopyTo(buf);
        buf[Magic.Length] = KindFile;
        var o = Magic.Length + 1;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(o, 2), (ushort)mime.Length);
        o += 2;
        mime.CopyTo(buf.AsSpan(o));
        o += mime.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(o, 2), (ushort)name.Length);
        o += 2;
        name.CopyTo(buf.AsSpan(o));
        o += name.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o, 4), (uint)fileBytes.Length);
        o += 4;
        fileBytes.CopyTo(buf.AsSpan(o));
        return buf;
    }

    public static byte[] EncodeTransferOffer(ChatWireTransferOffer offer)
    {
        return EncodeJsonFrame(KindTransferOffer, JsonSerializer.SerializeToUtf8Bytes(offer));
    }

    public static byte[] EncodeTransferControl(ChatWireTransferControl control)
    {
        return EncodeJsonFrame(KindTransferControl, JsonSerializer.SerializeToUtf8Bytes(control));
    }

    private static byte[] EncodeJsonFrame(byte kind, byte[] jsonUtf8)
    {
        var buf = new byte[Magic.Length + 1 + 4 + jsonUtf8.Length];
        Magic.CopyTo(buf);
        buf[Magic.Length] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(Magic.Length + 1, 4), (uint)jsonUtf8.Length);
        jsonUtf8.CopyTo(buf.AsSpan(Magic.Length + 1 + 4));
        return buf;
    }

    private static string NormalizeWireFileName(string fileName)
    {
        var n = Path.GetFileName(fileName.Trim());
        return string.IsNullOrEmpty(n) ? "file" : n;
    }

    /// <summary>Возвращает false для «наследуемых» сообщений (сырой UTF-8 без магии).</summary>
    public static bool TryParse(ReadOnlySpan<byte> payload, out ChatWireMessage? message)
    {
        message = null;
        if (payload.Length < Magic.Length + 1)
            return false;
        if (!payload.StartsWith(Magic))
            return false;

        var kind = payload[Magic.Length];
        var rest = payload.Slice(Magic.Length + 1);
        if (kind == KindText)
        {
            if (rest.Length < 4)
                return false;
            var len = BinaryPrimitives.ReadUInt32LittleEndian(rest);
            rest = rest.Slice(4);
            if (rest.Length < len)
                return false;
            var text = Encoding.UTF8.GetString(rest.Slice(0, (int)len));
            message = new ChatWireText(text);
            return true;
        }

        if (kind == KindImage)
        {
            if (rest.Length < 2)
                return false;
            var mimeLen = BinaryPrimitives.ReadUInt16LittleEndian(rest);
            rest = rest.Slice(2);
            if (rest.Length < mimeLen + 4)
                return false;
            var mime = Encoding.UTF8.GetString(rest.Slice(0, mimeLen));
            rest = rest.Slice(mimeLen);
            var imgLen = BinaryPrimitives.ReadUInt32LittleEndian(rest);
            rest = rest.Slice(4);
            if (rest.Length < imgLen)
                return false;
            message = new ChatWireImage(mime, rest.Slice(0, (int)imgLen).ToArray());
            return true;
        }

        if (kind == KindFile)
        {
            if (rest.Length < 2)
                return false;
            var mimeLen = BinaryPrimitives.ReadUInt16LittleEndian(rest);
            rest = rest.Slice(2);
            if (rest.Length < mimeLen + 2)
                return false;
            var mime = Encoding.UTF8.GetString(rest.Slice(0, mimeLen));
            rest = rest.Slice(mimeLen);
            var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(rest);
            rest = rest.Slice(2);
            if (rest.Length < nameLen + 4)
                return false;
            var wireName = Encoding.UTF8.GetString(rest.Slice(0, nameLen));
            rest = rest.Slice(nameLen);
            var fileLen = BinaryPrimitives.ReadUInt32LittleEndian(rest);
            rest = rest.Slice(4);
            if (rest.Length < fileLen)
                return false;
            message = new ChatWireFile(wireName, mime, rest.Slice(0, (int)fileLen).ToArray());
            return true;
        }

        if (kind is KindTransferOffer or KindTransferControl)
        {
            if (rest.Length < 4)
                return false;
            var len = BinaryPrimitives.ReadUInt32LittleEndian(rest);
            rest = rest.Slice(4);
            if (rest.Length < len)
                return false;
            var jsonBytes = rest.Slice(0, (int)len).ToArray();
            try
            {
                if (kind == KindTransferOffer)
                {
                    var offer = JsonSerializer.Deserialize<ChatWireTransferOffer>(jsonBytes);
                    if (offer == null)
                        return false;
                    message = offer;
                    return true;
                }

                var control = JsonSerializer.Deserialize<ChatWireTransferControl>(jsonBytes);
                if (control == null)
                    return false;
                message = control;
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}

public abstract record ChatWireMessage;

public sealed record ChatWireText(string Text) : ChatWireMessage;

public sealed record ChatWireImage(string MimeType, byte[] ImageBytes) : ChatWireMessage;

public sealed record ChatWireFile(string FileName, string MimeType, byte[] FileBytes) : ChatWireMessage;

public sealed record ChatWireTransferOffer(
    string TransferId,
    string TransferToken,
    string PayloadKind,
    string FileName,
    string MimeType,
    long SizeBytes,
    string Host,
    int Port,
    long ExpiresUtcTicks) : ChatWireMessage;

public sealed record ChatWireTransferControl(
    string Command,
    string TransferId,
    string TransferToken,
    string Host,
    int Port,
    long ExpiresUtcTicks,
    string ErrorCode) : ChatWireMessage;
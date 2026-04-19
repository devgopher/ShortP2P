using System.Buffers.Binary;
using System.Text;

namespace ShortP2P.Client.ChatMedia;

/// <summary>Бинарный формат полезной нагрузки внутри шифрованного messenger-кадра (совместимость со старыми клиентами: без префикса = UTF-8 текст).</summary>
public static class ChatWireCodec
{
    private static ReadOnlySpan<byte> Magic => "S2P1"u8;

    private const byte KindText = 0x01;
    private const byte KindImage = 0x02;

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

        return false;
    }
}

public abstract record ChatWireMessage;

public sealed record ChatWireText(string Text) : ChatWireMessage;

public sealed record ChatWireImage(string MimeType, byte[] ImageBytes) : ChatWireMessage;

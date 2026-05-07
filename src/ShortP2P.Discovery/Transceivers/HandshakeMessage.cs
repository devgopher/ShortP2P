using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>Тип кадра, передаваемого через <see cref="HandshakeMessage" />.</summary>
public enum HandshakeKind : byte
{
    /// <summary>RSA-зашифрованный handshake-пакет инициатора (0x01 + 128 байт).</summary>
    Handshake = 0x01,

    /// <summary>Запрос на установку сессии от подписчика лидеру (0x04 + 16 байт Guid).</summary>
    SessionSetupRequest = 0x04
}

/// <summary>
///     Кадр установки крипто-сессии. <see cref="Body" /> хранит payload без 1-байтного frame-маркера.
/// </summary>
public sealed class HandshakeMessage(HandshakeKind kind, ReadOnlyMemory<byte> body, TransportAddress remoteAddress)
{
    public HandshakeKind Kind { get; } = kind;

    /// <summary>Полезная нагрузка без байта <see cref="HandshakeKind" /> в начале.</summary>
    public ReadOnlyMemory<byte> Body { get; } = body;

    public TransportAddress RemoteAddress { get; } = remoteAddress;
}

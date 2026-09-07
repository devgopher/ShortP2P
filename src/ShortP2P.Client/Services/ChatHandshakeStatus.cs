namespace ShortP2P.Client.Services;

/// <summary>Состояние RSA/crypto handshake для UI чата.</summary>
public enum ChatHandshakeStatus
{
    /// <summary>Сессия ещё не запускалась.</summary>
    Idle = 0,

    /// <summary>Invite / RSA handshake / установка messenger в процессе.</summary>
    InProgress = 1,

    /// <summary>Криптосессия установлена (можно шифровать трафик).</summary>
    Established = 2
}

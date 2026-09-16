namespace ShortP2P.Auth.Data;

/// <summary>Лимиты полей профиля абонента (Avatar / AboutMe).</summary>
public static class PeerProfileLimits
{
    public const int MaxAboutMeChars = 250;

    /// <summary>Максимум UTF-8 байт для AboutMe на wire (250 символов × до 4 байт).</summary>
    public const int MaxAboutMeUtf8Bytes = MaxAboutMeChars * 4;

    /// <summary>Максимум байт аватара (локально и на wire 0x45).</summary>
    public const int MaxAvatarBytes = 20 * 1024;
}

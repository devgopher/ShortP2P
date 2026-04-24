namespace ShortP2P.Discovery;

/// <summary>
///     Уникальный номер абонента в сети: 16 байт (UUID в бинарном виде), для UI — короткая base64url-строка.
/// </summary>
public readonly struct CompressedNetworkId(Guid value) : IEquatable<CompressedNetworkId>
{
    public Guid Value { get; } = value;

    public static CompressedNetworkId New()
    {
        return new CompressedNetworkId(Guid.NewGuid());
    }

    public static CompressedNetworkId FromGuid(Guid g)
    {
        return new CompressedNetworkId(g);
    }

    public static CompressedNetworkId FromWireBytes(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length != 16
            ? throw new ArgumentException("Wire form must be exactly 16 bytes.", nameof(bytes))
            : new CompressedNetworkId(new Guid(bytes));
    }

    public bool TryWriteBytes(Span<byte> destination)
    {
        return Value.TryWriteBytes(destination);
    }

    public byte[] ToWireBytes()
    {
        var b = new byte[16];
        return !Value.TryWriteBytes(b) ? throw new InvalidOperationException("Failed to serialize Guid.") : b;
    }

    /// <summary>
    ///     Сжатое отображение для подписей и списков (безопасно для URL).
    /// </summary>
    public string ToShortString()
    {
        Span<byte> buf = stackalloc byte[16];
        return !Value.TryWriteBytes(buf) ? throw new InvalidOperationException() : ToBase64Url(buf);
    }

    public static CompressedNetworkId FromShortString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = FromBase64Url(text);
        return FromWireBytes(bytes);
    }

    public static bool operator ==(CompressedNetworkId a, CompressedNetworkId b)
    {
        return a.Value == b.Value;
    }

    public static bool operator !=(CompressedNetworkId a, CompressedNetworkId b)
    {
        return a.Value != b.Value;
    }

    public bool Equals(CompressedNetworkId other)
    {
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is CompressedNetworkId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return ToShortString();
    }

    private static string ToBase64Url(ReadOnlySpan<byte> data)
    {
        var s = Convert.ToBase64String(data);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static ReadOnlySpan<byte> FromBase64Url(string text)
    {
        var t = text.Replace("-", "+", StringComparison.Ordinal).Replace("_", "/", StringComparison.Ordinal);
        switch (t.Length % 4)
        {
            case 2:
                t += "==";
                break;
            case 3:
                t += "=";
                break;
        }

        var bytes = Convert.FromBase64String(t);
        
        return bytes.Length != 16 ? throw new FormatException("Decoded id must be 16 bytes.") : bytes;
    }
}
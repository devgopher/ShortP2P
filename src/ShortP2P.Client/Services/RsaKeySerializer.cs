using System.Text.Json;
using ShortP2P.Crypto;

namespace ShortP2P.Client.Services;

public static class RsaKeySerializer
{
    private sealed class PublicDto
    {
        public string M { get; set; } = "";
        public string E { get; set; } = "";
    }

    private sealed class PrivateDto
    {
        public string M { get; set; } = "";
        public string E { get; set; } = "";
        public string D { get; set; } = "";
        public string P { get; set; } = "";
        public string Q { get; set; } = "";
        public string DP { get; set; } = "";
        public string DQ { get; set; } = "";
        public string IQ { get; set; } = "";
    }

    public static string SerializePublic(RsaPublicKey key)
    {
        var dto = new PublicDto
        {
            M = Convert.ToBase64String(key.Modulus),
            E = Convert.ToBase64String(key.Exponent),
        };
        return JsonSerializer.Serialize(dto);
    }

    public static RsaPublicKey DeserializePublic(string json)
    {
        var dto = JsonSerializer.Deserialize<PublicDto>(json) ?? throw new FormatException("Invalid public key JSON.");
        return new RsaPublicKey(Convert.FromBase64String(dto.M), Convert.FromBase64String(dto.E));
    }

    public static string SerializePrivate(RsaPrivateKey key)
    {
        var dto = new PrivateDto
        {
            M = Convert.ToBase64String(key.Modulus),
            E = Convert.ToBase64String(key.Exponent),
            D = Convert.ToBase64String(key.D),
            P = Convert.ToBase64String(key.P),
            Q = Convert.ToBase64String(key.Q),
            DP = Convert.ToBase64String(key.DP),
            DQ = Convert.ToBase64String(key.DQ),
            IQ = Convert.ToBase64String(key.InverseQ),
        };
        return JsonSerializer.Serialize(dto);
    }

    public static RsaPrivateKey DeserializePrivate(string json)
    {
        var dto = JsonSerializer.Deserialize<PrivateDto>(json) ?? throw new FormatException("Invalid private key JSON.");
        var p = new System.Security.Cryptography.RSAParameters
        {
            Modulus = Convert.FromBase64String(dto.M),
            Exponent = Convert.FromBase64String(dto.E),
            D = Convert.FromBase64String(dto.D),
            P = Convert.FromBase64String(dto.P),
            Q = Convert.FromBase64String(dto.Q),
            DP = Convert.FromBase64String(dto.DP),
            DQ = Convert.FromBase64String(dto.DQ),
            InverseQ = Convert.FromBase64String(dto.IQ),
        };
        return new RsaPrivateKey(p);
    }
}

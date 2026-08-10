using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShortP2P.MessengerServer.Api.Options;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Auth;

public sealed class JwtAuthTokenService(IOptions<AuthOptions> options, IClock clock) : IAuthTokenService
{
    public const string NetworkIdClaimType = "network_id";

    public AuthToken IssueToken(string networkId)
    {
        var opts = options.Value;
        var expires = clock.UtcNow.Add(opts.TokenLifetime);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, networkId),
            new Claim(NetworkIdClaimType, networkId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            notBefore: clock.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new AuthToken(encoded, expires);
    }
}

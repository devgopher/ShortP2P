using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShortP2P.MessengerServer.Auth;
using ShortP2P.MessengerServer.Auth.Options;
using ShortP2P.MessengerServer.Tests.Fakes;

namespace ShortP2P.MessengerServer.Tests.Auth;

public class JwtAuthTokenServiceTests
{
    [Fact]
    public void IssueToken_ContainsNetworkAndDeviceClaims()
    {
        var clock = new FakeClock(TestHarness.T0);
        var options = Options.Create(new AuthOptions
        {
            Issuer = "iss",
            Audience = "aud",
            SigningKey = "ShortP2P-Test-Signing-Key-32chars!!",
            TokenLifetime = TimeSpan.FromMinutes(30)
        });
        var service = new JwtAuthTokenService(options, clock);

        var token = service.IssueToken("net-a", TestIds.DeviceA);

        Assert.Equal(TestHarness.T0.AddMinutes(30), token.ExpiresAtUtc);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(
            token.Token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "iss",
                ValidateAudience = true,
                ValidAudience = "aud",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(options.Value.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (_, _, _, _) => true
            },
            out var validated);

        Assert.Equal("net-a", principal.FindFirst(JwtAuthTokenService.NetworkIdClaimType)?.Value);
        Assert.Equal(TestIds.DeviceA, principal.FindFirst(JwtAuthTokenService.DeviceIdClaimType)?.Value);
        Assert.Equal("net-a", ((JwtSecurityToken)validated).Subject);
    }
}
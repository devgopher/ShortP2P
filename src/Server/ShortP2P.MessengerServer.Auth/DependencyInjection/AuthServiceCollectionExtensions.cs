using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShortP2P.MessengerServer.Auth.Options;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Auth.DependencyInjection;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers password hashing (salt+hash) and JWT bearer authentication from the <c>Auth</c> section.
    /// Continue with a persistence provider: <c>WithEntityFrameworkDb()</c> or <c>WithLiteDb()</c>.
    /// </summary>
    public static AuthBuilder AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));

        var authOptions = new AuthOptions();
        configuration.GetSection(AuthOptions.Section).Bind(authOptions);

        if (string.IsNullOrWhiteSpace(authOptions.SigningKey) || authOptions.SigningKey.Length < 32)
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 characters.");

        services.AddSingleton<IPasswordHasher, CryptoPasswordHasher>();
        services.AddSingleton<IAuthTokenService, JwtAuthTokenService>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = authOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = JwtAuthTokenService.NetworkIdClaimType
                };
            });

        services.AddAuthorization();
        return new AuthBuilder(services, configuration);
    }
}

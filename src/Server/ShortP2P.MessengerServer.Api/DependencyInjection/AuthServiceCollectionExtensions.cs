using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ShortP2P.MessengerServer.Api.Auth;
using ShortP2P.MessengerServer.Api.Options;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.DependencyInjection;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers password hashing (salt+hash), JWT token service, certificate reader,
    /// and JWT bearer authentication from the <c>Auth</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
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
        services.AddSingleton<IServerCertificateReader, KestrelServerCertificateReader>();

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
        return services;
    }

    /// <summary>Adds Swagger with JWT bearer security scheme.</summary>
    public static IServiceCollection AddMessengerSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ShortP2P Messenger Server API",
                Version = "v1",
                Description = "HTTPS store-and-forward messenger API."
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });

        return services;
    }
}

using ShortP2P.MessengerServer.Api.DependencyInjection;
using ShortP2P.MessengerServer.Api.Filters;
using ShortP2P.MessengerServer.Auth.DependencyInjection;
using ShortP2P.MessengerServer.Auth.LiteDB.DependencyInjection;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Infrastructure.HostPowers;
using ShortP2P.MessengerServer.UseCases.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    // Long-poll can hold connections up to MaxPollTimeoutSeconds (~30s).
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
    options.Limits.MaxRequestBodySize = BlobLimits.MaxCiphertextBytes;
});

builder.Services
    .AddInfrastructure(builder.Configuration)
    .WithInMemoryCache()
    .WithCachePromotion();

builder.Services.Configure<MessengerInboxOptions>(
    builder.Configuration.GetSection(MessengerInboxOptions.Section));

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddHostPowersLiteDb(builder.Configuration);

builder.Services
    .AddAuth(builder.Configuration)
    .WithLiteDb();

builder.Services.AddServerCertificateReader();
builder.Services.AddMessengerUseCases();
builder.Services.AddMessengerSwagger();
builder.Services.AddScoped<PresenceTouchFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<PresenceTouchFilter>();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ShortP2P Messenger Server v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

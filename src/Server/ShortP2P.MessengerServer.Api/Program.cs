using ShortP2P.MessengerServer.Api.DependencyInjection;
using ShortP2P.MessengerServer.Api.Endpoints;
using ShortP2P.MessengerServer.Auth.DependencyInjection;
using ShortP2P.MessengerServer.Auth.LiteDB.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

builder.Services
    .AddInfrastructure(builder.Configuration)
    .WithInMemoryCache()
    .WithCachePromotion();

builder.Services.AddPersistence(builder.Configuration);

builder.Services
    .AddAuth(builder.Configuration)
    .WithLiteDb();

builder.Services.AddServerCertificateReader();
builder.Services.AddMessengerUseCases();
builder.Services.AddMessengerSwagger();

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
app.MapMessengerApi();

app.Run();

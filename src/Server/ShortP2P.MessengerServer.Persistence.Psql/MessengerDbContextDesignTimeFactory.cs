using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShortP2P.MessengerServer.Persistence.Psql;

public sealed class MessengerDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MessengerDbContext>
{
    public MessengerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MESSENGER_DB")
            ?? "Host=localhost;Port=5432;Database=shortp2p_messenger;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MessengerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MessengerDbContext(options);
    }
}

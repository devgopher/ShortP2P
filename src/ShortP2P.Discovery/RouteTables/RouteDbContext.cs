using Microsoft.EntityFrameworkCore;
using ShortP2P.Discovery;

namespace ShortP2P.Discovery.RouteTables;

public class RouteDbContext : DbContext
{
    public RouteDbContext(DbContextOptions<RouteDbContext> options)
        : base(options)
    {
    }

    public DbSet<Route> Routes => Set<Route>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Route>(entity =>
        {
            entity.HasKey(e => e.RouteId);
            entity.OwnsMany(e => e.PeerRoutes, pr =>
            {
                pr.WithOwner().HasForeignKey(p => p.RouteId);
                pr.Property(p => p.PeerAddress).IsRequired();
                pr.OwnsOne(p => p.PeerIdentity, pi =>
                {
                    pi.Property(p => p.Nickname).IsRequired();
                    pi.Property(p => p.DataUdpPort);
                    pi.Property(p => p.NetworkId)
                        .HasConversion(id => id.Value, v => CompressedNetworkId.FromGuid(v));
                });
            });
        });
    }
}

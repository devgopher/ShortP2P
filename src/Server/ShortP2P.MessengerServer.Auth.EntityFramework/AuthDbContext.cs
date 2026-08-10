using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Auth.EntityFramework.Entities;

namespace ShortP2P.MessengerServer.Auth.EntityFramework;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<AuthAccountEntity> Accounts => Set<AuthAccountEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthAccountEntity>(e =>
        {
            e.ToTable("auth_accounts");
            e.HasKey(x => x.NetworkId);
            e.HasIndex(x => x.Nick).IsUnique();
            e.Property(x => x.NetworkId).HasMaxLength(64);
            e.Property(x => x.Nick).HasMaxLength(256);
            e.Property(x => x.PasswordSalt).HasMaxLength(128);
            e.Property(x => x.PasswordHash).HasMaxLength(128);
        });
    }
}

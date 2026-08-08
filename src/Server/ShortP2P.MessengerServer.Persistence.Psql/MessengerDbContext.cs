using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;

namespace ShortP2P.MessengerServer.Persistence.Psql;

public sealed class MessengerDbContext(DbContextOptions<MessengerDbContext> options) : DbContext(options)
{
    public DbSet<ClientAccountRecord> ClientAccounts => Set<ClientAccountRecord>();
    public DbSet<ChatRecord> Chats => Set<ChatRecord>();
    public DbSet<ChatRequestRecord> ChatRequests => Set<ChatRequestRecord>();
    public DbSet<CryptoKeysRecord> CryptoKeys => Set<CryptoKeysRecord>();
    public DbSet<ClientStatusRecord> ClientStatuses => Set<ClientStatusRecord>();
    public DbSet<MessageRecord> Messages => Set<MessageRecord>();
    public DbSet<DeliveryTicketRecord> DeliveryTickets => Set<DeliveryTicketRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientAccountRecord>(e =>
        {
            e.ToTable("client_accounts");
            e.HasKey(x => x.NetworkId);
            e.HasIndex(x => x.Nick).IsUnique();
            e.Property(x => x.NetworkId).HasMaxLength(64);
            e.Property(x => x.Nick).HasMaxLength(256);
            e.Property(x => x.PasswordSalt).HasMaxLength(128);
            e.Property(x => x.PasswordHash).HasMaxLength(128);
        });

        modelBuilder.Entity<ChatRecord>(e =>
        {
            e.ToTable("chats");
            e.HasKey(x => x.ChatId);
            e.Property(x => x.ChatId).HasMaxLength(64);
            e.Property(x => x.NetworkIds).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ChatRequestRecord>(e =>
        {
            e.ToTable("chat_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.RequesterNetworkId).HasMaxLength(64);
            e.Property(x => x.TargetNetworkId).HasMaxLength(64);
            e.HasIndex(x => x.TargetNetworkId);
            e.HasIndex(x => new { x.RequesterNetworkId, x.TargetNetworkId });
        });

        modelBuilder.Entity<CryptoKeysRecord>(e =>
        {
            e.ToTable("crypto_keys");
            e.HasKey(x => new { x.SrcNetworkId, x.TgtNetworkId });
            e.Property(x => x.SrcNetworkId).HasMaxLength(64);
            e.Property(x => x.TgtNetworkId).HasMaxLength(64);
        });

        modelBuilder.Entity<ClientStatusRecord>(e =>
        {
            e.ToTable("client_statuses");
            e.HasKey(x => x.NetworkId);
            e.Property(x => x.NetworkId).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<MessageRecord>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasMaxLength(128);
            e.Property(x => x.SrcNetworkId).HasMaxLength(64);
            e.Property(x => x.TgtNetworkId).HasMaxLength(64);
            e.HasIndex(x => x.TgtNetworkId);
            e.HasIndex(x => x.SrcNetworkId);
        });

        modelBuilder.Entity<DeliveryTicketRecord>(e =>
        {
            e.ToTable("delivery_tickets");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasMaxLength(128);
        });
    }
}

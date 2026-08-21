using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;

namespace ShortP2P.MessengerServer.Persistence.Psql;

public sealed class MessengerDbContext(DbContextOptions<MessengerDbContext> options) : DbContext(options)
{
    public DbSet<ChatRecord> Chats => Set<ChatRecord>();
    public DbSet<ChatRequestRecord> ChatRequests => Set<ChatRequestRecord>();
    public DbSet<ChatRequestInboxRecord> ChatRequestInboxes => Set<ChatRequestInboxRecord>();
    public DbSet<CryptoKeysRecord> CryptoKeys => Set<CryptoKeysRecord>();
    public DbSet<ClientStatusRecord> ClientStatuses => Set<ClientStatusRecord>();
    public DbSet<MessageRecord> Messages => Set<MessageRecord>();
    public DbSet<MessageInboxRecord> MessageInboxes => Set<MessageInboxRecord>();
    public DbSet<DeliveryTicketRecord> DeliveryTickets => Set<DeliveryTicketRecord>();
    public DbSet<BlobRecord> Blobs => Set<BlobRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
            e.HasKey(x => x.RequestId);
            e.Property(x => x.RequestId).HasMaxLength(64);
            e.Property(x => x.RequesterNetworkId).HasMaxLength(64);
            e.Property(x => x.TargetNetworkId).HasMaxLength(64);
            e.HasIndex(x => x.TargetNetworkId);
            e.HasIndex(x => new { x.RequesterNetworkId, x.TargetNetworkId });
        });

        modelBuilder.Entity<ChatRequestInboxRecord>(e =>
        {
            e.ToTable("chat_request_inbox");
            e.HasKey(x => new { x.RequestId, x.DeviceId });
            e.Property(x => x.RequestId).HasMaxLength(64);
            e.Property(x => x.TargetNetworkId).HasMaxLength(64);
            e.Property(x => x.DeviceId).HasMaxLength(64);
            e.HasIndex(x => new { x.TargetNetworkId, x.DeviceId });
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
            e.HasKey(x => new { x.NetworkId, x.DeviceId });
            e.Property(x => x.NetworkId).HasMaxLength(64);
            e.Property(x => x.DeviceId).HasMaxLength(64);
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
            e.HasIndex(x => x.CreatedUtc);
        });

        modelBuilder.Entity<MessageInboxRecord>(e =>
        {
            e.ToTable("message_inbox");
            e.HasKey(x => new { x.MessageId, x.DeviceId });
            e.Property(x => x.MessageId).HasMaxLength(128);
            e.Property(x => x.TgtNetworkId).HasMaxLength(64);
            e.Property(x => x.DeviceId).HasMaxLength(64);
            e.HasIndex(x => new { x.TgtNetworkId, x.DeviceId });
        });

        modelBuilder.Entity<DeliveryTicketRecord>(e =>
        {
            e.ToTable("delivery_tickets");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasMaxLength(128);
        });

        modelBuilder.Entity<BlobRecord>(e =>
        {
            e.ToTable("blobs");
            e.HasKey(x => x.BlobId);
            e.Property(x => x.BlobId).HasMaxLength(128);
            e.Property(x => x.SrcNetworkId).HasMaxLength(64);
            e.Property(x => x.TgtNetworkId).HasMaxLength(64);
            e.Property(x => x.Ciphertext).HasColumnType("bytea");
            e.HasIndex(x => x.CreatedUtc);
            e.HasIndex(x => x.TgtNetworkId);
        });
    }
}

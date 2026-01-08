using Microsoft.EntityFrameworkCore;

namespace AlchemyCallbackTest.Forwarder
{
    public sealed class CallbackForwarderDbContext : DbContext
    {
        public CallbackForwarderDbContext(DbContextOptions<CallbackForwarderDbContext> options) : base(options)
        {
        }

        public DbSet<RawWebhookEventEntity> RawWebhookEvents { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("forwarder");

            modelBuilder.Entity<RawWebhookEventEntity>(entity =>
            {
                entity.ToTable("raw_webhook_events", "forwarder");
                entity.HasKey(e => e.Id);
                // Use application-generated GUIDs to avoid pgcrypto dependency
                entity.Property(e => e.Id);
                entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
                entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
                entity.Property(e => e.EventData).HasColumnType("jsonb").IsRequired();
                entity.Property(e => e.EventHash).HasMaxLength(64).IsRequired();
                entity.Property(e => e.ReceivedAt).HasDefaultValueSql("NOW()");
                entity.Property(e => e.SourceIp).HasColumnType("inet");
                entity.Property(e => e.Headers).HasColumnType("jsonb");

                // Indexes for performance and deduplication
                entity.HasIndex(e => e.ReceivedAt).HasDatabaseName("idx_raw_webhook_events_received_at");
                entity.HasIndex(e => e.Provider).HasDatabaseName("idx_raw_webhook_events_provider");
                entity.HasIndex(e => e.EventType).HasDatabaseName("idx_raw_webhook_events_event_type");
                entity.HasIndex(e => new { e.Provider, e.EventHash })
                      .IsUnique()
                      .HasDatabaseName("idx_raw_webhook_events_provider_hash");
            });
        }
    }
}

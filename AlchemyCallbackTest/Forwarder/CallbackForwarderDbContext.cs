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
                entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
                entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
                entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
                entity.Property(e => e.EventData).HasColumnType("jsonb").IsRequired();
                entity.Property(e => e.EventHash).HasMaxLength(64).IsRequired();
                entity.Property(e => e.ReceivedAt).HasDefaultValueSql("NOW()");
                entity.Property(e => e.SourceIp).HasColumnType("inet");
                entity.Property(e => e.Headers).HasColumnType("jsonb");
            });
        }
    }
}

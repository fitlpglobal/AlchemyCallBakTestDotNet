using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AlchemyCallbackTest.Forwarder;

namespace AlchemyCallbackTest.Forwarder.Migrations
{
    [DbContext(typeof(CallbackForwarderDbContext))]
    public class CallbackForwarderDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("forwarder");

            modelBuilder.Entity<RawWebhookEventEntity>(b =>
            {
                b.ToTable("raw_webhook_events", "forwarder");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
                b.Property(e => e.Provider).HasMaxLength(50).IsRequired();
                b.Property(e => e.EventType).HasMaxLength(100).IsRequired();
                b.Property(e => e.EventData).HasColumnType("jsonb").IsRequired();
                b.Property(e => e.EventHash).HasMaxLength(64).IsRequired();
                b.Property(e => e.ReceivedAt).HasColumnType("timestamptz").HasDefaultValueSql("NOW()");
                b.Property(e => e.SourceIp).HasColumnType("inet");
                b.Property(e => e.Headers).HasColumnType("jsonb");

                b.HasIndex(e => e.ReceivedAt).HasDatabaseName("idx_raw_webhook_events_received_at");
                b.HasIndex(e => e.Provider).HasDatabaseName("idx_raw_webhook_events_provider");
                b.HasIndex(e => e.EventType).HasDatabaseName("idx_raw_webhook_events_event_type");
                b.HasIndex(e => new { e.Provider, e.EventHash }).IsUnique().HasDatabaseName("idx_raw_webhook_events_provider_hash");
            });
        }
    }
}

using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlchemyCallbackTest.Domain;
using AlchemyCallbackTest.Forwarder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlchemyCallbackTest.Persistence
{
    public sealed class EventRepository : IEventRepository
    {
        private readonly CallbackForwarderDbContext _context;
        private readonly ILogger<EventRepository> _logger;
        private readonly IRetryPolicy _retryPolicy;

        public EventRepository(
            CallbackForwarderDbContext context,
            ILogger<EventRepository> logger,
            IRetryPolicy retryPolicy)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        }

        public async Task<string> StoreEventAsync(RawWebhookEvent webhookEvent, CancellationToken cancellationToken = default)
        {
            if (webhookEvent is null) throw new ArgumentNullException(nameof(webhookEvent));

            return await _retryPolicy.ExecuteAsync(async () =>
            {
                var entity = MapToEntity(webhookEvent);

                _context.RawWebhookEvents.Add(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("Stored webhook event {EventId} from provider {Provider}",
                    entity.Id, webhookEvent.Provider);

                return entity.Id.ToString();
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> EventHashExistsAsync(string eventHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventHash)) throw new ArgumentException("Event hash is required", nameof(eventHash));

            return await _retryPolicy.ExecuteAsync(async () =>
            {
                return await _context.RawWebhookEvents
                    .AnyAsync(e => e.EventHash == eventHash, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> CheckHealthAsync()
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync("SELECT 1").ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database health check failed");
                return false;
            }
        }

        public async Task<long> GetRecentEventCountAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                return await _context.RawWebhookEvents
                    .LongCountAsync(e => e.ReceivedAt >= since, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        private static RawWebhookEventEntity MapToEntity(RawWebhookEvent webhookEvent)
        {
            IPAddress? sourceIp = null;
            if (!string.IsNullOrWhiteSpace(webhookEvent.SourceIp) && IPAddress.TryParse(webhookEvent.SourceIp, out var parsed))
            {
                sourceIp = parsed;
            }

            return new RawWebhookEventEntity
            {
                Id = string.IsNullOrWhiteSpace(webhookEvent.Id) ? Guid.NewGuid() : Guid.Parse(webhookEvent.Id),
                Provider = webhookEvent.Provider,
                EventType = webhookEvent.EventType,
                EventData = webhookEvent.EventData,
                EventHash = webhookEvent.EventHash,
                ReceivedAt = webhookEvent.ReceivedAt,
                SourceIp = sourceIp,
                Headers = JsonSerializer.Serialize(webhookEvent.Headers)
            };
        }
    }
}

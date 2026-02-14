using System;
using System.Threading;
using System.Threading.Tasks;
using AlchemyCallbackTest.Domain;

namespace AlchemyCallbackTest.Persistence
{
    public interface IEventRepository
    {
        Task<string> StoreEventAsync(RawWebhookEvent webhookEvent, CancellationToken cancellationToken = default);
        Task<bool> EventHashExistsAsync(string eventHash, CancellationToken cancellationToken = default);
        Task<bool> CheckHealthAsync();
        Task<long> GetRecentEventCountAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
    }
}

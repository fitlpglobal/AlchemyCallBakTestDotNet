using System.Threading;
using System.Threading.Tasks;

namespace AlchemyCallbackTest.Services
{
    public interface IDuplicateDetectionService
    {
        string ComputeEventHash(string eventData);
        Task<bool> IsDuplicateAsync(string provider, string eventData, CancellationToken cancellationToken = default);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlchemyCallbackTest.Persistence
{
    public interface IRetryPolicy
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
    }
}

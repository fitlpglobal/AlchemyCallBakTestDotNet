using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlchemyCallbackTest.Persistence
{
    public sealed class RetryPolicy : IRetryPolicy
    {
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;
        private readonly double _backoffMultiplier;
        private readonly TimeSpan _maxDelay;

        public RetryPolicy(int maxRetries = 3, int initialDelayMilliseconds = 100, double backoffMultiplier = 2.0, int maxDelayMilliseconds = 5000)
        {
            _maxRetries = Math.Max(1, maxRetries);
            _initialDelay = TimeSpan.FromMilliseconds(Math.Max(1, initialDelayMilliseconds));
            _backoffMultiplier = backoffMultiplier <= 1 ? 2.0 : backoffMultiplier;
            _maxDelay = TimeSpan.FromMilliseconds(Math.Max(1, maxDelayMilliseconds));
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (operation is null) throw new ArgumentNullException(nameof(operation));

            var attempt = 0;
            var delay = _initialDelay;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransient(ex) && attempt < _maxRetries && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    var nextDelayMs = Math.Min(delay.TotalMilliseconds * _backoffMultiplier, _maxDelay.TotalMilliseconds);
                    delay = TimeSpan.FromMilliseconds(nextDelayMs);
                }
            }
        }

        private static bool IsTransient(Exception ex)
        {
            if (ex is TimeoutException)
            {
                return true;
            }

            if (ex.InnerException is TimeoutException)
            {
                return true;
            }

            return ex is DbUpdateException
                   || ex is NpgsqlException
                   || ex is PostgresException;
        }
    }
}

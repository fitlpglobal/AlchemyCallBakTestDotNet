using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlchemyCallbackTest.Persistence;

namespace AlchemyCallbackTest.Services
{
    public sealed class DuplicateDetectionService : IDuplicateDetectionService
    {
        private readonly IEventRepository _eventRepository;
        private readonly ConcurrentDictionary<string, byte> _recentHashes = new();
        private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<string, DateTimeOffset> _hashTimestamps = new();

        public DuplicateDetectionService(IEventRepository eventRepository)
        {
            _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        }

        public string ComputeEventHash(string eventData)
        {
            if (eventData is null) throw new ArgumentNullException(nameof(eventData));

            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(eventData);
            var hashBytes = sha256.ComputeHash(bytes);

            var charPool = ArrayPool<char>.Shared;
            var chars = charPool.Rent(hashBytes.Length * 2);
            try
            {
                var span = chars.AsSpan();
                var index = 0;
                foreach (var b in hashBytes)
                {
                    span[index++] = GetHexChar(b >> 4);
                    span[index++] = GetHexChar(b & 0xF);
                }

                return new string(span[..(hashBytes.Length * 2)]);
            }
            finally
            {
                charPool.Return(chars);
            }
        }

        public async Task<bool> IsDuplicateAsync(string provider, string eventData, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("Provider is required", nameof(provider));
            if (eventData is null) throw new ArgumentNullException(nameof(eventData));

            var hash = ComputeEventHash(eventData);
            var cacheKey = GetCacheKey(provider, hash);

            CleanupExpiredEntries();

            if (_recentHashes.ContainsKey(cacheKey))
            {
                return true;
            }

            var existsInDatabase = await _eventRepository.EventHashExistsAsync(hash, cancellationToken).ConfigureAwait(false);

            if (existsInDatabase)
            {
                _recentHashes[cacheKey] = 0;
                _hashTimestamps[cacheKey] = DateTimeOffset.UtcNow;
                return true;
            }

            _recentHashes[cacheKey] = 0;
            _hashTimestamps[cacheKey] = DateTimeOffset.UtcNow;
            return false;
        }

        private static char GetHexChar(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
        }

        private static string GetCacheKey(string provider, string hash)
        {
            return provider + ":" + hash;
        }

        private void CleanupExpiredEntries()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _hashTimestamps)
            {
                if (now - kvp.Value > _cacheTtl)
                {
                    _hashTimestamps.TryRemove(kvp.Key, out _);
                    _recentHashes.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlchemyCallbackTest.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlchemyCallbackTest.Services
{
    public sealed class WebhookAuthenticationService : IWebhookAuthenticationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebhookAuthenticationService> _logger;
        private readonly bool _authEnabled;
        private readonly HashSet<string> _allowedIps;

        private const string EnableAuthKey = "CALLBACK_FORWARDER_ENABLE_AUTH";
        private const string AllowedIpsKey = "CALLBACK_FORWARDER_ALLOWED_IPS"; // comma-separated list

        public WebhookAuthenticationService(IConfiguration configuration, ILogger<WebhookAuthenticationService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var enableAuthValue = _configuration[EnableAuthKey];
            if (!string.IsNullOrWhiteSpace(enableAuthValue) && bool.TryParse(enableAuthValue, out var enabled))
            {
                _authEnabled = enabled;
            }
            else
            {
                _authEnabled = false; // fail-open by default
            }

            var ipsValue = _configuration[AllowedIpsKey];
            _allowedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(ipsValue))
            {
                foreach (var ip in ipsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    _allowedIps.Add(ip);
                }
            }
        }

        public Task<AuthenticationResult> AuthenticateAsync(IncomingWebhookEvent incomingEvent, CancellationToken cancellationToken = default)
        {
            if (incomingEvent is null) throw new ArgumentNullException(nameof(incomingEvent));

            // If authentication is disabled, always succeed but log once per provider.
            if (!_authEnabled)
            {
                _logger.LogDebug("Authentication disabled; accepting webhook from provider {Provider}.", incomingEvent.Provider);
                return Task.FromResult(new AuthenticationResult
                {
                    IsAuthenticated = true,
                    AuthenticatedProvider = incomingEvent.Provider,
                    FailureReason = null
                });
            }

            // Resolve secret for provider (supports env naming: CALLBACK_FORWARDER_SECRET_<PROVIDER>)
            var providerKey = (incomingEvent.Provider ?? string.Empty).Trim();
            var secret = ResolveSecret(providerKey);

            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogWarning("Authentication enabled but no secret configured for provider {Provider}. Failing open.", providerKey);
                return Task.FromResult(new AuthenticationResult
                {
                    IsAuthenticated = true,
                    AuthenticatedProvider = incomingEvent.Provider,
                    FailureReason = "No secret configured; authentication bypassed"
                });
            }

            // Verify signature if present
            if (string.IsNullOrWhiteSpace(incomingEvent.Signature))
            {
                _logger.LogWarning("Missing signature for provider {Provider} while authentication is enabled.", providerKey);
                return Task.FromResult(new AuthenticationResult
                {
                    IsAuthenticated = false,
                    AuthenticatedProvider = incomingEvent.Provider,
                    FailureReason = "Missing signature"
                });
            }

            var expectedSignature = ComputeHmacSha256Hex(secret, incomingEvent.EventData ?? string.Empty);
            var providedSignature = NormalizeSignature(incomingEvent.Signature);

            if (!FixedTimeEquals(expectedSignature, providedSignature))
            {
                _logger.LogWarning("Invalid signature for provider {Provider}.", providerKey);
                return Task.FromResult(new AuthenticationResult
                {
                    IsAuthenticated = false,
                    AuthenticatedProvider = incomingEvent.Provider,
                    FailureReason = "Invalid signature"
                });
            }

            // Optional IP allowlist check
            if (_allowedIps.Count > 0 && !string.IsNullOrWhiteSpace(incomingEvent.SourceIp))
            {
                if (!_allowedIps.Contains(incomingEvent.SourceIp))
                {
                    _logger.LogWarning("IP {Ip} not in allowlist for provider {Provider}.", incomingEvent.SourceIp, providerKey);
                    return Task.FromResult(new AuthenticationResult
                    {
                        IsAuthenticated = false,
                        AuthenticatedProvider = incomingEvent.Provider,
                        FailureReason = "IP not allowed"
                    });
                }
            }

            return Task.FromResult(new AuthenticationResult
            {
                IsAuthenticated = true,
                AuthenticatedProvider = incomingEvent.Provider,
                FailureReason = null
            });
        }

        private static string NormalizeSignature(string signature)
        {
            const string prefix = "sha256=";
            if (signature.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return signature.Substring(prefix.Length);
            }
            return signature.Trim();
        }

        private static string ComputeHmacSha256Hex(string secret, string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = Encoding.UTF8.GetBytes(payload);
            var hashBytes = hmac.ComputeHash(bytes);

            var sb = new StringBuilder(hashBytes.Length * 2);
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            var result = 0;
            for (var i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }

        private string? ResolveSecret(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return null;
            }

            var upper = provider.ToUpperInvariant();

            // Primary: environment variable CALLBACK_FORWARDER_SECRET_<PROVIDER>
            var envKey = $"CALLBACK_FORWARDER_SECRET_{upper}";
            var secret = _configuration[envKey];
            if (!string.IsNullOrWhiteSpace(secret))
            {
                return secret;
            }

            // Fallback: configuration section CallbackForwarder:Authentication:ProviderSecrets:<provider>
            var configKey = $"CallbackForwarder:Authentication:ProviderSecrets:{provider}";
            secret = _configuration[configKey];
            if (!string.IsNullOrWhiteSpace(secret))
            {
                return secret;
            }

            return null;
        }
    }
}

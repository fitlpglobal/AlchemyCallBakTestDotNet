# Design Document - Callback Forwarder Service

## Overview

The Callback Forwarder Service is a lightweight ASP.NET Core 8 service that receives webhook events from blockchain providers (primarily Alchemy) and persists them to the shared PostgreSQL database for the Non-Custodial USDT Payment Gateway system. This service acts as a "dumb" ingestion layer focused solely on reliable event capture without interpretation or processing. It ensures high-throughput event ingestion, duplicate detection, webhook authentication, and comprehensive monitoring while maintaining simplicity and reliability.

The service serves as the critical entry point for all blockchain events, ensuring that no events are lost even during high-volume periods or downstream processing failures. It provides the foundation for the event-driven payment detection system by reliably capturing and storing raw webhook data for later processing by the Invoicing Facade API.

Finalized decisions:
- Maintain simple, focused responsibility for event ingestion only
- Use high-performance, write-optimized database operations
- Implement comprehensive duplicate detection with fallback mechanisms
- Provide webhook signature verification with IP allowlist fallback
- Support high-throughput processing (1000+ events/second) with minimal latency

## Architecture

### High-Level Architecture

The service follows a simple, focused architecture optimized for high-throughput ingestion:

```
┌─────────────────────────────────────────────────────────┐
│         HTTP API Layer                                  │
│  - WebhookController                                    │
│  - HealthController                                     │
│  - Authentication middleware                            │
│  - Rate limiting middleware                             │
└─────────────────────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────┐
│         Event Processing Layer                          │
│  - WebhookEventProcessor                               │
│  - Duplicate detection                                 │
│  - Event validation                                    │
│  - Signature verification                              │
└─────────────────────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────┐
│         Persistence Layer                               │
│  - EventRepository                                     │
│  - Transaction management                              │
│  - Connection pooling                                  │
│  - Retry logic                                         │
└─────────────────────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────┐
│         Shared PostgreSQL Database                     │
│  - raw_webhook_events table                           │
│  - Optimized indexes                                   │
│  - JSONB event storage                                 │
│  - Write-optimized schema                              │
└─────────────────────────────────────────────────────────┘
```

### Design Principles

1. **Single Responsibility**: Focus exclusively on reliable event ingestion
2. **High Performance**: Optimized for throughput and minimal latency
3. **Reliability**: Comprehensive error handling, retry logic, and monitoring
4. **Simplicity**: Minimal complexity, no business logic or external API calls
5. **Security**: Webhook signature verification and IP allowlist support
6. **Observability**: Comprehensive logging, metrics, and health monitoring
7. **Fail-Safe**: Prefer storing events over rejecting them when in doubt

## Components and Interfaces

### HTTP API Layer

#### WebhookController
```csharp
[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookEventProcessor _eventProcessor;
    private readonly ILogger<WebhookController> _logger;
    
    public WebhookController(
        IWebhookEventProcessor eventProcessor,
        ILogger<WebhookController> logger)
    {
        _eventProcessor = eventProcessor;
        _logger = logger;
    }
    
    /// <summary>
    /// Receives webhook events from blockchain providers.
    /// </summary>
    /// <param name="provider">Provider name (e.g., "alchemy")</param>
    /// <param name="eventType">Event type from provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP status indicating processing result</returns>
    [HttpPost("{provider}/{eventType}")]
    public async Task<IActionResult> ReceiveWebhook(
        string provider,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        var correlationId = HttpContext.TraceIdentifier;
        
        try
        {
            // Read raw request body
            using var reader = new StreamReader(Request.Body);
            var eventData = await reader.ReadToEndAsync(cancellationToken);
            
            if (string.IsNullOrWhiteSpace(eventData))
            {
                _logger.LogWarning("Received empty webhook event from provider {Provider}", provider);
                return BadRequest(new { error = "Event data is required", correlationId });
            }
            
            // Extract signature for verification
            var signature = Request.Headers["X-Signature"].FirstOrDefault() ??
                           Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            
            // Process the event
            var result = await _eventProcessor.ProcessEventAsync(new IncomingWebhookEvent
            {
                Provider = provider,
                EventType = eventType,
                EventData = eventData,
                Signature = signature,
                SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                ReceivedAt = DateTimeOffset.UtcNow,
                Headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())
            }, cancellationToken);
            
            return result.Success switch
            {
                true when result.WasDuplicate => Ok(new { message = "Event already processed", correlationId }),
                true => Ok(new { message = "Event processed successfully", eventId = result.EventId, correlationId }),
                false => StatusCode(500, new { error = result.ErrorMessage, correlationId })
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized webhook request from provider {Provider}", provider);
            return Unauthorized(new { error = "Authentication failed", correlationId });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid webhook request from provider {Provider}", provider);
            return BadRequest(new { error = ex.Message, correlationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process webhook from provider {Provider}", provider);
            return StatusCode(500, new { error = "Internal server error", correlationId });
        }
    }
    
    /// <summary>
    /// Health check endpoint for monitoring.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        var health = await _eventProcessor.GetHealthStatusAsync();
        
        return health.IsHealthy 
            ? Ok(health) 
            : StatusCode(503, health);
    }
}
```

#### HealthController
```csharp
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IWebhookEventProcessor _eventProcessor;
    
    public HealthController(IWebhookEventProcessor eventProcessor)
    {
        _eventProcessor = eventProcessor;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetHealth()
    {
        var health = await _eventProcessor.GetHealthStatusAsync();
        
        var response = new
        {
            status = health.IsHealthy ? "healthy" : "unhealthy",
            timestamp = DateTimeOffset.UtcNow,
            details = health
        };
        
        return health.IsHealthy ? Ok(response) : StatusCode(503, response);
    }
}
```

### Event Processing Layer

#### IWebhookEventProcessor
```csharp
public interface IWebhookEventProcessor
{
    /// <summary>
    /// Processes an incoming webhook event.
    /// </summary>
    /// <param name="incomingEvent">Event data from provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing result</returns>
    Task<EventProcessingResult> ProcessEventAsync(IncomingWebhookEvent incomingEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets current health status of the processor.
    /// </summary>
    /// <returns>Health status details</returns>
    Task<HealthStatus> GetHealthStatusAsync();
    
    /// <summary>
    /// Gets current ingestion metrics.
    /// </summary>
    /// <returns>Performance and throughput metrics</returns>
    Task<IngestionMetrics> GetMetricsAsync();
}
```

#### WebhookEventProcessor
```csharp
public sealed class WebhookEventProcessor : IWebhookEventProcessor
{
    private readonly IEventRepository _eventRepository;
    private readonly IWebhookAuthenticationService _authService;
    private readonly IDuplicateDetectionService _duplicateService;
    private readonly ILogger<WebhookEventProcessor> _logger;
    private readonly CallbackForwarderOptions _options;
    private readonly IMetricsCollector _metrics;
    
    public WebhookEventProcessor(
        IEventRepository eventRepository,
        IWebhookAuthenticationService authService,
        IDuplicateDetectionService duplicateService,
        ILogger<WebhookEventProcessor> logger,
        IOptions<CallbackForwarderOptions> options,
        IMetricsCollector metrics)
    {
        _eventRepository = eventRepository;
        _authService = authService;
        _duplicateService = duplicateService;
        _logger = logger;
        _options = options.Value;
        _metrics = metrics;
    }
    
    public async Task<EventProcessingResult> ProcessEventAsync(IncomingWebhookEvent incomingEvent, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Authenticate the request
            var authResult = await _authService.AuthenticateAsync(incomingEvent, cancellationToken);
            if (!authResult.IsAuthenticated)
            {
                _logger.LogWarning("Authentication failed for provider {Provider} from IP {SourceIp}: {Reason}",
                    incomingEvent.Provider, incomingEvent.SourceIp, authResult.FailureReason);
                
                _metrics.IncrementCounter("webhook_auth_failures", new[] { ("provider", incomingEvent.Provider) });
                throw new UnauthorizedAccessException(authResult.FailureReason);
            }
            
            // Check for duplicates
            var isDuplicate = await _duplicateService.IsDuplicateAsync(incomingEvent, cancellationToken);
            if (isDuplicate)
            {
                _logger.LogDebug("Duplicate event detected from provider {Provider}", incomingEvent.Provider);
                _metrics.IncrementCounter("webhook_duplicates", new[] { ("provider", incomingEvent.Provider) });
                
                return new EventProcessingResult
                {
                    Success = true,
                    WasDuplicate = true,
                    EventId = null
                };
            }
            
            // Store the event
            var eventId = await _eventRepository.StoreEventAsync(new RawWebhookEvent
            {
                Provider = incomingEvent.Provider,
                EventType = incomingEvent.EventType,
                EventData = incomingEvent.EventData,
                EventHash = _duplicateService.ComputeEventHash(incomingEvent.EventData),
                ReceivedAt = incomingEvent.ReceivedAt,
                SourceIp = incomingEvent.SourceIp,
                Headers = incomingEvent.Headers
            }, cancellationToken);
            
            stopwatch.Stop();
            
            _logger.LogDebug("Processed webhook event {EventId} from provider {Provider} in {Duration}ms",
                eventId, incomingEvent.Provider, stopwatch.ElapsedMilliseconds);
            
            _metrics.RecordHistogram("webhook_processing_duration", stopwatch.ElapsedMilliseconds,
                new[] { ("provider", incomingEvent.Provider) });
            _metrics.IncrementCounter("webhook_events_processed", new[] { ("provider", incomingEvent.Provider) });
            
            return new EventProcessingResult
            {
                Success = true,
                WasDuplicate = false,
                EventId = eventId
            };
        }
        catch (Exception ex) when (!(ex is UnauthorizedAccessException))
        {
            stopwatch.Stop();
            
            _logger.LogError(ex, "Failed to process webhook event from provider {Provider}", incomingEvent.Provider);
            _metrics.IncrementCounter("webhook_processing_errors", new[] { ("provider", incomingEvent.Provider) });
            
            return new EventProcessingResult
            {
                Success = false,
                WasDuplicate = false,
                EventId = null,
                ErrorMessage = "Event processing failed"
            };
        }
    }
    
    public async Task<HealthStatus> GetHealthStatusAsync()
    {
        var dbHealthy = await _eventRepository.CheckHealthAsync();
        var recentErrorRate = await _metrics.GetErrorRateAsync(TimeSpan.FromMinutes(5));
        var currentThroughput = await _metrics.GetThroughputAsync(TimeSpan.FromMinutes(1));
        
        var isHealthy = dbHealthy && recentErrorRate < _options.MaxErrorRateThreshold;
        
        return new HealthStatus
        {
            IsHealthy = isHealthy,
            DatabaseConnected = dbHealthy,
            ErrorRate = recentErrorRate,
            CurrentThroughput = currentThroughput,
            LastChecked = DateTimeOffset.UtcNow
        };
    }
}
```

### Models and DTOs

```csharp
public sealed record IncomingWebhookEvent
{
    public required string Provider { get; init; }
    public required string EventType { get; init; }
    public required string EventData { get; init; }
    public string? Signature { get; init; }
    public string? SourceIp { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
}

public sealed record RawWebhookEvent
{
    public string? Id { get; init; }
    public required string Provider { get; init; }
    public required string EventType { get; init; }
    public required string EventData { get; init; }
    public required string EventHash { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public string? SourceIp { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
}

public sealed record EventProcessingResult
{
    public bool Success { get; init; }
    public bool WasDuplicate { get; init; }
    public string? EventId { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record AuthenticationResult
{
    public bool IsAuthenticated { get; init; }
    public string? FailureReason { get; init; }
    public string? AuthenticatedProvider { get; init; }
}

public sealed record HealthStatus
{
    public bool IsHealthy { get; init; }
    public bool DatabaseConnected { get; init; }
    public double ErrorRate { get; init; }
    public double CurrentThroughput { get; init; }
    public DateTimeOffset LastChecked { get; init; }
}

public sealed record IngestionMetrics
{
    public long TotalEventsProcessed { get; init; }
    public long EventsProcessedLastMinute { get; init; }
    public long DuplicatesDetected { get; init; }
    public long AuthenticationFailures { get; init; }
    public double AverageProcessingTimeMs { get; init; }
    public double ErrorRate { get; init; }
}
```

### Persistence Layer

#### IEventRepository
```csharp
public interface IEventRepository
{
    /// <summary>
    /// Stores a raw webhook event in the database.
    /// </summary>
    /// <param name="webhookEvent">Event to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated event ID</returns>
    Task<string> StoreEventAsync(RawWebhookEvent webhookEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if an event hash already exists.
    /// </summary>
    /// <param name="eventHash">Event content hash</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if hash exists</returns>
    Task<bool> EventHashExistsAsync(string eventHash, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks database connectivity and health.
    /// </summary>
    /// <returns>True if database is accessible</returns>
    Task<bool> CheckHealthAsync();
    
    /// <summary>
    /// Gets recent events for monitoring purposes.
    /// </summary>
    /// <param name="since">Time threshold for recent events</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Count of recent events</returns>
    Task<long> GetRecentEventCountAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
```

#### EventRepository
```csharp
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
        _context = context;
        _logger = logger;
        _retryPolicy = retryPolicy;
    }
    
    public async Task<string> StoreEventAsync(RawWebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var entity = new RawWebhookEventEntity
            {
                Id = Guid.NewGuid(),
                Provider = webhookEvent.Provider,
                EventType = webhookEvent.EventType,
                EventData = webhookEvent.EventData,
                EventHash = webhookEvent.EventHash,
                ReceivedAt = webhookEvent.ReceivedAt,
                SourceIp = webhookEvent.SourceIp,
                Headers = JsonSerializer.Serialize(webhookEvent.Headers)
            };
            
            _context.RawWebhookEvents.Add(entity);
            await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogDebug("Stored webhook event {EventId} from provider {Provider}",
                entity.Id, webhookEvent.Provider);
            
            return entity.Id.ToString();
        }, cancellationToken);
    }
    
    public async Task<bool> EventHashExistsAsync(string eventHash, CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            return await _context.RawWebhookEvents
                .AnyAsync(e => e.EventHash == eventHash, cancellationToken);
        }, cancellationToken);
    }
    
    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            await _context.Database.ExecuteSqlRawAsync("SELECT 1");
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
                .CountAsync(e => e.ReceivedAt >= since, cancellationToken);
        }, cancellationToken);
    }
}
```

## Data Models

### Database Schema

#### raw_webhook_events table
```sql
CREATE TABLE raw_webhook_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider VARCHAR(50) NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    event_data JSONB NOT NULL,
    event_hash VARCHAR(64) NOT NULL,
    received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    source_ip INET,
    headers JSONB
);

-- Indexes for performance
CREATE INDEX idx_raw_webhook_events_received_at ON raw_webhook_events(received_at DESC);
CREATE INDEX idx_raw_webhook_events_provider ON raw_webhook_events(provider);
CREATE INDEX idx_raw_webhook_events_event_type ON raw_webhook_events(event_type);
CREATE UNIQUE INDEX idx_raw_webhook_events_hash ON raw_webhook_events(event_hash);

-- Index for efficient duplicate detection
CREATE INDEX idx_raw_webhook_events_provider_hash ON raw_webhook_events(provider, event_hash);
```

### Configuration Models

```csharp
public sealed class CallbackForwarderOptions
{
    public const string SectionName = "CallbackForwarder";
    
    public AuthenticationOptions Authentication { get; set; } = new();
    public PerformanceOptions Performance { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();
    public MonitoringOptions Monitoring { get; set; } = new();
}

public sealed class AuthenticationOptions
{
    public bool RequireSignature { get; set; } = true;
    public Dictionary<string, string> ProviderSecrets { get; set; } = new();
    public List<string> AllowedIpRanges { get; set; } = new();
    public bool LogSecurityWarnings { get; set; } = true;
}

public sealed class PerformanceOptions
{
    public int MaxConcurrentRequests { get; set; } = 100;
    public int MaxRequestSizeBytes { get; set; } = 1024 * 1024; // 1MB
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int DatabaseConnectionPoolSize { get; set; } = 20;
}

public sealed class RetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);
    public double BackoffMultiplier { get; set; } = 2.0;
}

public sealed class MonitoringOptions
{
    public double MaxErrorRateThreshold { get; set; } = 0.05; // 5%
    public TimeSpan MetricsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
    public bool EnableDetailedMetrics { get; set; } = true;
}
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system-essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: HTTP status code correctness
*For any* webhook request, the service should return appropriate HTTP status codes based on processing results
**Validates: Requirements 1.2, 1.3, 1.4, 1.5**

### Property 2: Event persistence completeness
*For any* successfully received webhook event, the complete JSON payload should be persisted to the database
**Validates: Requirements 2.1, 2.2**

### Property 3: Transaction consistency
*For any* database write operation, successful completion should result in immediate transaction commit
**Validates: Requirements 2.3**

### Property 4: Error response on database failure
*For any* database write failure, the service should return error responses to providers
**Validates: Requirements 2.4**

### Property 5: Metadata preservation
*For any* stored event, provider metadata and timestamps should be included in the database record
**Validates: Requirements 2.5**

### Property 6: Duplicate detection by event ID
*For any* event with a provider-supplied ID, duplicate events should be detected and handled appropriately
**Validates: Requirements 3.1, 3.2**

### Property 7: Content hash deduplication
*For any* event without provider IDs, duplicate detection should use content hashing as fallback
**Validates: Requirements 3.3**

### Property 8: Duplicate logging
*For any* duplicate event detection, the occurrence should be logged appropriately
**Validates: Requirements 3.4**

### Property 9: Fail-safe duplicate handling
*For any* duplicate detection failure, the service should err on the side of storing events
**Validates: Requirements 3.5**

### Property 10: Signature verification
*For any* webhook with provider signatures, the service should verify signatures before processing
**Validates: Requirements 4.1, 4.2**

### Property 11: IP allowlist fallback
*For any* webhook without signatures, the service should check IP allowlists for authentication
**Validates: Requirements 4.3**

### Property 12: Authentication success processing
*For any* successfully authenticated webhook, the service should process events normally
**Validates: Requirements 4.4**

### Property 13: No data transformation
*For any* event processing, the service should not interpret or transform the event data
**Validates: Requirements 5.1, 5.2**

### Property 14: No external API calls
*For any* event processing, the service should not make calls to external APIs
**Validates: Requirements 5.3**

### Property 15: High throughput capability
*For any* load scenario, the service should handle at least 1000 events per second
**Validates: Requirements 6.1**

### Property 16: Concurrent processing
*For any* concurrent webhook requests, the service should process them in parallel without interference
**Validates: Requirements 6.2**

### Property 17: Connection pooling usage
*For any* database operations, the service should use connection pooling for performance
**Validates: Requirements 6.3**

### Property 18: Event metadata logging
*For any* received event, the service should log appropriate metadata
**Validates: Requirements 7.1**

### Property 19: Error logging detail
*For any* error condition, the service should log detailed error information
**Validates: Requirements 7.2**

### Property 20: Structured JSON logging
*For any* log entry, the service should use structured JSON format
**Validates: Requirements 7.5**

### Property 21: Database index optimization
*For any* event storage, the service should use appropriate database indexes for performance
**Validates: Requirements 9.1**

### Property 22: Timestamp-based query efficiency
*For any* timestamp-based event query, the service should support efficient lookups
**Validates: Requirements 9.2**

### Property 23: JSONB storage format
*For any* event data storage, the service should use JSONB format for flexible querying
**Validates: Requirements 9.3**

### Property 24: Retry logic with exponential backoff
*For any* database connection failure, the service should retry with exponential backoff
**Validates: Requirements 10.1**

### Property 25: Error classification for retries
*For any* error condition, the service should distinguish between transient and permanent failures
**Validates: Requirements 10.2**

### Property 26: Correlation ID inclusion
*For any* error log entry, the service should include correlation IDs for tracing
**Validates: Requirements 10.4**

## Testing Strategy

### Unit Testing
- **Event Processing Logic**: Test event validation, duplicate detection, and storage
- **Authentication**: Test signature verification and IP allowlist checking
- **Error Handling**: Test exception handling and error response generation
- **Retry Logic**: Test database retry behavior with various failure scenarios

### Integration Testing
- **Database Operations**: Test event storage and retrieval with real PostgreSQL
- **HTTP Endpoints**: Test webhook endpoints with various payload formats
- **Performance**: Test throughput and concurrent request handling
- **Health Monitoring**: Test health check endpoints and metrics collection

### Load Testing
- **Throughput Testing**: Verify 1000+ events/second capability
- **Concurrent Load**: Test behavior under high concurrent request load
- **Memory Usage**: Verify memory efficiency under sustained load
- **Database Performance**: Test database performance under write-heavy load

### Property-Based Testing
- **Duplicate Detection**: Generate various event modifications and verify detection
- **Authentication**: Test signature verification with various signature formats
- **Error Recovery**: Test retry behavior with random failure patterns
- **Data Integrity**: Verify event data preservation through storage and retrieval

## Implementation Notes

### Dependency Injection Registration

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCallbackForwarder(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options
        services.Configure<CallbackForwarderOptions>(configuration.GetSection(CallbackForwarderOptions.SectionName));
        
        // Register database context
        services.AddDbContext<CallbackForwarderDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("Database"));
            options.EnableServiceProviderCaching();
            options.EnableSensitiveDataLogging(false);
        });
        
        // Register services
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IWebhookEventProcessor, WebhookEventProcessor>();
        services.AddSingleton<IDuplicateDetectionService, DuplicateDetectionService>();
        services.AddSingleton<IWebhookAuthenticationService, WebhookAuthenticationService>();
        services.AddSingleton<IRetryPolicy, ExponentialBackoffRetryPolicy>();
        services.AddSingleton<IMetricsCollector, MetricsCollector>();
        
        // Configure HTTP client settings
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = 1024 * 1024; // 1MB
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        });
        
        return services;
    }
}
```

### High-Performance Event Processing

```csharp
public sealed class OptimizedEventProcessor : IWebhookEventProcessor
{
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly ObjectPool<StringBuilder> _stringBuilderPool;
    private readonly ConcurrentDictionary<string, byte> _recentHashes;
    
    public OptimizedEventProcessor(IOptions<CallbackForwarderOptions> options)
    {
        var maxConcurrency = options.Value.Performance.MaxConcurrentRequests;
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _stringBuilderPool = new DefaultObjectPool<StringBuilder>(new StringBuilderPooledObjectPolicy());
        _recentHashes = new ConcurrentDictionary<string, byte>();
    }
    
    public async Task<EventProcessingResult> ProcessEventAsync(IncomingWebhookEvent incomingEvent, CancellationToken cancellationToken = default)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            // Fast path for duplicate detection using in-memory cache
            var eventHash = ComputeEventHash(incomingEvent.EventData);
            if (_recentHashes.ContainsKey(eventHash))
            {
                return new EventProcessingResult { Success = true, WasDuplicate = true };
            }
            
            // Process event with optimized database operations
            var result = await ProcessEventInternal(incomingEvent, eventHash, cancellationToken);
            
            // Cache hash for fast duplicate detection
            if (result.Success && !result.WasDuplicate)
            {
                _recentHashes.TryAdd(eventHash, 0);
                
                // Periodically clean cache to prevent memory growth
                if (_recentHashes.Count > 10000)
                {
                    _ = Task.Run(() => CleanHashCache());
                }
            }
            
            return result;
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }
    
    private string ComputeEventHash(string eventData)
    {
        var stringBuilder = _stringBuilderPool.Get();
        try
        {
            // Use pooled StringBuilder for efficient hash computation
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(eventData));
            
            stringBuilder.Clear();
            foreach (var b in hashBytes)
            {
                stringBuilder.Append(b.ToString("x2"));
            }
            
            return stringBuilder.ToString();
        }
        finally
        {
            _stringBuilderPool.Return(stringBuilder);
        }
    }
}
```

### Database Optimization

```csharp
public sealed class CallbackForwarderDbContext : DbContext
{
    public DbSet<RawWebhookEventEntity> RawWebhookEvents { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RawWebhookEventEntity>(entity =>
        {
            entity.ToTable("raw_webhook_events");
            
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            
            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EventData).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.EventHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ReceivedAt).HasDefaultValueSql("NOW()");
            entity.Property(e => e.SourceIp).HasColumnType("inet");
            entity.Property(e => e.Headers).HasColumnType("jsonb");
            
            // Indexes for performance
            entity.HasIndex(e => e.ReceivedAt).HasDatabaseName("idx_raw_webhook_events_received_at");
            entity.HasIndex(e => e.Provider).HasDatabaseName("idx_raw_webhook_events_provider");
            entity.HasIndex(e => e.EventHash).IsUnique().HasDatabaseName("idx_raw_webhook_events_hash");
        });
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Optimize for write-heavy workload
        optionsBuilder.UseNpgsql(options =>
        {
            options.CommandTimeout(30);
        });
    }
}
```
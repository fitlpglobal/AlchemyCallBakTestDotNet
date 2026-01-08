# Implementation Tasks - Callback Forwarder Service Upgrade

## Overview

This task list upgrades the existing `AlchemyCallBakTestDotNet` project from a basic console-logging webhook receiver to a production-ready callback forwarder service that meets the design specification. The service is already deployed on Railway and connected to GitHub, so we need to upgrade it incrementally while maintaining compatibility.

**IMPORTANT ARCHITECTURE GUARDRAIL:**
This Callback Forwarder service shares the Railway Postgres instance with other services. It MUST only create/read/write tables in schema "forwarder" (e.g., forwarder.raw_webhook_events). Do NOT scaffold or generate EF models for other schemas/tables. Use a separate EF migrations history table for this service.

## Implementation Plan

- [ ] 0. Enable Swagger (OpenAPI) with production feature flag
  - Add Swashbuckle and register `AddEndpointsApiExplorer` + `AddSwaggerGen`
  - Gate Swagger middleware by env var `ENABLE_SWAGGER=true` (on Railway)
  - Keep existing `POST /webhook/alchemy` endpoint intact
  - Verify Swagger UI at `/swagger` in Railway
  - _Supports: live testing after each step_

- [ ] 1. Set up database infrastructure with schema isolation
  - Add Entity Framework Core packages for PostgreSQL
  - Create CallbackForwarderDbContext with schema isolation ("forwarder")
  - Configure separate migrations history table for this service
  - Create RawWebhookEventEntity model
  - Add connection string configuration for Railway DATABASE_URL
  - _Requirements: 2.1, 8.1, 9.1, 9.3_

- [ ] 2. Implement database schema and migrations
  - Create initial migration for forwarder.raw_webhook_events table
  - Add indexes for performance (received_at DESC, provider, event_hash unique)
  - Configure JSONB columns for event_data and headers
  - Test migration against Railway PostgreSQL
  - _Requirements: 9.1, 9.2, 9.3_

- [ ] 3. Implement core domain models and DTOs
  - Create IncomingWebhookEvent record for HTTP requests
  - Create RawWebhookEvent record for database storage
  - Create EventProcessingResult record for operation results
  - Create AuthenticationResult and HealthStatus records
  - Add proper JSON serialization attributes
  - _Requirements: 2.2, 2.5, 7.1_

- [ ] 4. Implement event repository with retry logic
  - Create IEventRepository interface
  - Implement EventRepository with EF Core operations
  - Add exponential backoff retry policy for database operations
  - Implement StoreEventAsync with transaction handling
  - Add EventHashExistsAsync for duplicate detection
  - Add CheckHealthAsync for monitoring
  - _Requirements: 2.1, 2.3, 2.4, 10.1, 10.2, 10.3_

- [ ] 5. Implement duplicate detection service
  - Create IDuplicateDetectionService interface
  - Implement content-based hashing for events without IDs
  - Add in-memory cache for recent event hashes (performance optimization)
  - Implement IsDuplicateAsync with database fallback
  - Add ComputeEventHash method using SHA256
  - _Requirements: 3.1, 3.3, 3.4, 3.5_

- [ ] 6. Implement webhook authentication service
  - Create IWebhookAuthenticationService interface
  - Add signature verification for Alchemy webhooks (HMAC-SHA256)
  - Implement IP allowlist checking as fallback
  - Add configuration for provider secrets and allowed IP ranges
  - Handle authentication bypass scenarios with security logging
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

- [ ] 7. Implement core webhook event processor
  - Create IWebhookEventProcessor interface
  - Implement WebhookEventProcessor with full processing pipeline
  - Add authentication → duplicate detection → storage flow
  - Implement ProcessEventAsync with comprehensive error handling
  - Add GetHealthStatusAsync and GetMetricsAsync methods
  - _Requirements: 5.1, 5.2, 5.3, 6.1, 6.2_

- [ ] 8. Upgrade HTTP API layer with proper controllers
  - Replace minimal API with proper WebhookController
  - Add comprehensive error handling and status code mapping
  - Implement correlation ID generation and propagation
  - Add request validation and size limits
  - Support multiple provider endpoints (not just Alchemy)
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 10.4_

- [ ] 9. Implement health monitoring and metrics
  - Create HealthController with detailed health checks
  - Add IMetricsCollector interface and implementation
  - Implement performance metrics (throughput, error rate, latency)
  - Add database connectivity monitoring
  - Expose metrics for Railway monitoring
  - _Requirements: 6.5, 7.3, 7.4, 8.3, 8.4_

- [ ] 10. Implement structured logging with Serilog
  - Replace console logging with Serilog structured logging
  - Configure JSON output for Railway log aggregation
  - Add correlation ID enrichment to all log entries
  - Implement security-aware logging (no sensitive data)
  - Add performance and error logging throughout pipeline
  - _Requirements: 7.1, 7.2, 7.5, 10.4_

- [ ] 11. Add configuration management and options
  - Create CallbackForwarderOptions configuration classes
  - Add AuthenticationOptions, PerformanceOptions, RetryOptions
  - Configure environment variable binding for Railway
  - Add validation for required configuration values
  - Support development vs production configuration
  - _Requirements: 8.2, 4.1, 6.3, 10.1_

- [ ] 12. Implement performance optimizations
  - Add connection pooling configuration for PostgreSQL
  - Implement concurrency limiting with SemaphoreSlim
  - Add object pooling for StringBuilder and other reusable objects
  - Configure Kestrel for high-throughput scenarios
  - Add memory-efficient event processing
  - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [ ] 13. Add comprehensive error handling and resilience
  - Implement global exception handler with proper status codes
  - Add circuit breaker pattern for database operations
  - Implement graceful degradation when database is unavailable
  - Add proper exception types and error context
  - Ensure fail-safe behavior (prefer storing events)
  - _Requirements: 2.4, 3.5, 10.1, 10.2, 10.5_

- [ ] 14. Implement dependency injection and service registration
  - Create ServiceCollectionExtensions for clean DI registration
  - Register all services with appropriate lifetimes
  - Configure EF Core with performance optimizations
  - Add health checks registration
  - Configure HTTP client settings and timeouts
  - _Requirements: 8.1, 6.3, 7.3_

- [ ] 15. Add comprehensive request validation and security
  - Implement request size limits and timeout handling
  - Add input sanitization and validation
  - Implement rate limiting per IP/provider
  - Add security headers and CORS configuration
  - Validate JSON payload structure before processing
  - _Requirements: 1.5, 4.1, 4.5, 6.1_

- [ ] 16. Update deployment configuration for Railway
  - Update Dockerfile for production optimizations
  - Add environment variable configuration for Railway
  - Configure health check endpoint for Railway monitoring
  - Add startup validation for database connectivity
  - Update port configuration and startup scripts
  - _Requirements: 8.2, 8.3, 8.4, 8.5_

- [ ] 17. Implement data archiving and retention policies
  - Add configuration for event retention periods
  - Implement background service for old event cleanup
  - Add archiving capabilities for long-term storage
  - Configure automatic cleanup schedules
  - Add monitoring for storage usage
  - _Requirements: 9.5_

- [ ] 18. Add comprehensive testing infrastructure
  - Create unit tests for all service components
  - Add integration tests with TestContainers PostgreSQL
  - Implement load testing for throughput requirements (1000+ events/sec)
  - Add property-based tests for duplicate detection
  - Create end-to-end tests with real Alchemy webhook payloads
  - _Requirements: 6.1, 3.1, 1.2_

- [ ] 19. Implement monitoring and observability
  - Add application insights or equivalent monitoring
  - Implement custom metrics for business KPIs
  - Add alerting for error rate thresholds
  - Create dashboards for operational monitoring
  - Add distributed tracing support
  - _Requirements: 6.5, 7.4, 10.4_

- [ ] 20. Final integration and deployment validation
  - Test end-to-end flow with Railway PostgreSQL
  - Validate schema isolation with other services
  - Perform load testing against Railway deployment
  - Verify health checks and monitoring integration
  - Test failover and recovery scenarios
  - Document operational procedures
  - _Requirements: 8.1, 6.1, 10.5_

## Property-Based Tests Implementation

- [ ]* 21. Write property-based tests for correctness properties
  - **Property 2: HTTP status code correctness** - Test various request scenarios return appropriate status codes
  - **Property 6: Duplicate detection by event ID** - Test duplicate detection with various event ID patterns
  - **Property 7: Content hash deduplication** - Test content-based duplicate detection
  - **Property 15: High throughput capability** - Test sustained 1000+ events/second processing
  - **Property 16: Concurrent processing** - Test parallel request handling without interference
  - **Property 24: Retry logic with exponential backoff** - Test database retry behavior
  - Use FsCheck.Xunit for property generation and validation
  - _Requirements: 1.2, 3.1, 3.3, 6.1, 6.2, 10.1_

## Migration Strategy

### Phase 1: Database Foundation (Tasks 1-4)
- Set up database infrastructure without breaking existing functionality
- Keep existing console logging while adding database persistence
- Test database connectivity and schema isolation

### Phase 2: Core Services (Tasks 5-8)
- Implement business logic services
- Upgrade API layer incrementally
- Maintain backward compatibility with existing webhook endpoint

### Phase 3: Production Features (Tasks 9-16)
- Add monitoring, logging, and performance optimizations
- Implement security and resilience features
- Update deployment configuration

### Phase 4: Testing and Validation (Tasks 17-20)
- Comprehensive testing and validation
- Performance testing and optimization
- Final deployment and monitoring setup

## Key Implementation Notes

### PR & Deployment Cadence
- Create a feature branch (e.g., `feature/callback-forwarder-upgrade`).
- Commit and push after each numbered step (0, 1, 2, ...), open/refresh a PR.
- Keep `/webhook/alchemy` fully functional at all times (see Guardrails).
- Enable Swagger on Railway with `ENABLE_SWAGGER=true` for live testing.

### Schema Isolation Requirements
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // CRITICAL: Use forwarder schema for all tables
    modelBuilder.HasDefaultSchema("forwarder");
    
    // Configure separate migrations history table
    modelBuilder.Entity<RawWebhookEventEntity>(entity =>
    {
        entity.ToTable("raw_webhook_events", "forwarder");
        // ... entity configuration
    });
}
```

### Railway Environment Variables
```bash
# Required environment variables for Railway deployment
DATABASE_URL=postgresql://...  # Provided by Railway PostgreSQL plugin
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
CALLBACK_FORWARDER_MAX_THROUGHPUT=1000
CALLBACK_FORWARDER_ENABLE_AUTH=true
```

### Backward Compatibility
- Keep existing `/webhook/alchemy` endpoint during migration
- Gradually add new features without breaking existing functionality
- Maintain same response format for existing integrations
- Add new endpoints alongside existing ones

### Performance Targets
- **Throughput**: 1000+ events per second sustained
- **Latency**: <100ms per event processing
- **Memory**: <500MB under normal load
- **Database**: <10ms average query time

This upgrade plan transforms the basic webhook receiver into a production-ready callback forwarder service while maintaining compatibility with the existing Railway deployment and GitHub integration.
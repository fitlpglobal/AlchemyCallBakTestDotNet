# Requirements Document - Callback Forwarder Service

## Introduction

The Callback Forwarder Service is a lightweight .NET service that receives webhook events from blockchain providers (primarily Alchemy) and persists them to the shared PostgreSQL database for the Non-Custodial USDT Payment Gateway system. This service acts as a "dumb" ingestion layer that focuses solely on reliable event capture without interpretation or processing. The service ensures that all blockchain events are preserved for later processing by the Invoicing Facade API.

## Glossary

- **Callback_Forwarder**: The webhook ingestion service
- **Provider_Webhook**: HTTP POST request from blockchain provider containing event data
- **Raw_Event_Data**: Unprocessed JSON payload from provider webhook
- **Event_Persistence**: Storage of raw events in PostgreSQL database
- **Webhook_Endpoint**: HTTP endpoint that receives provider callbacks
- **Event_Metadata**: Additional information about event source and timing
- **Duplicate_Detection**: Logic to identify and handle repeated events
- **Provider_Signature**: Cryptographic signature for webhook authentication
- **Ingestion_Rate**: Number of events processed per second
- **Shared_Database**: PostgreSQL instance used by both Invoicing and PaymentMethods APIs

## Requirements

### Requirement 1

**User Story:** As a blockchain provider, I want to send webhook events to a reliable endpoint, so that transaction notifications are delivered successfully.

#### Acceptance Criteria

1. WHEN providers send webhook events THEN the Callback_Forwarder SHALL accept HTTP POST requests
2. WHEN events are received THEN the Callback_Forwarder SHALL return appropriate HTTP status codes
3. WHEN event processing succeeds THEN the Callback_Forwarder SHALL return 200 OK
4. WHEN event processing fails THEN the Callback_Forwarder SHALL return 500 Internal Server Error
5. WHEN invalid requests are received THEN the Callback_Forwarder SHALL return 400 Bad Request

### Requirement 2

**User Story:** As a system operator, I want raw webhook events to be persisted immediately, so that no events are lost even if downstream processing fails.

#### Acceptance Criteria

1. WHEN webhook events are received THEN the Callback_Forwarder SHALL persist raw event data to the database
2. WHEN persisting events THEN the Callback_Forwarder SHALL store complete JSON payload
3. WHEN database writes succeed THEN the Callback_Forwarder SHALL commit transactions immediately
4. WHEN database writes fail THEN the Callback_Forwarder SHALL return error responses to providers
5. WHEN events are stored THEN the Callback_Forwarder SHALL include provider metadata and timestamps

### Requirement 3

**User Story:** As a reliability engineer, I want duplicate events to be handled safely, so that provider retries don't create data inconsistencies.

#### Acceptance Criteria

1. WHEN duplicate events are received THEN the Callback_Forwarder SHALL detect them using event IDs
2. WHEN duplicates are detected THEN the Callback_Forwarder SHALL return 200 OK without re-storing
3. WHEN event IDs are missing THEN the Callback_Forwarder SHALL use content hashing for deduplication
4. WHEN deduplication occurs THEN the Callback_Forwarder SHALL log duplicate detection
5. WHEN duplicate handling fails THEN the Callback_Forwarder SHALL err on the side of storing events

### Requirement 4

**User Story:** As a security engineer, I want webhook authentication to be verified, so that only legitimate provider events are accepted.

#### Acceptance Criteria

1. WHEN providers include signatures THEN the Callback_Forwarder SHALL verify webhook signatures
2. WHEN signature verification fails THEN the Callback_Forwarder SHALL return 401 Unauthorized
3. WHEN signatures are missing THEN the Callback_Forwarder SHALL check IP allowlists
4. WHEN authentication succeeds THEN the Callback_Forwarder SHALL process events normally
5. WHEN authentication is bypassed THEN the Callback_Forwarder SHALL log security warnings

### Requirement 5

**User Story:** As a system architect, I want the service to remain simple and focused, so that it doesn't become a complex processing bottleneck.

#### Acceptance Criteria

1. WHEN processing events THEN the Callback_Forwarder SHALL not interpret or transform event data
2. WHEN storing events THEN the Callback_Forwarder SHALL preserve original JSON structure
3. WHEN events are received THEN the Callback_Forwarder SHALL not call external APIs
4. WHEN business logic is needed THEN the Callback_Forwarder SHALL delegate to other services
5. WHEN complexity increases THEN the Callback_Forwarder SHALL maintain single responsibility

### Requirement 6

**User Story:** As a performance engineer, I want high-throughput event ingestion, so that the service can handle peak webhook volumes.

#### Acceptance Criteria

1. WHEN processing events THEN the Callback_Forwarder SHALL handle at least 1000 events per second
2. WHEN concurrent requests arrive THEN the Callback_Forwarder SHALL process them in parallel
3. WHEN database connections are used THEN the Callback_Forwarder SHALL use connection pooling
4. WHEN memory is allocated THEN the Callback_Forwarder SHALL minimize allocations per request
5. WHEN performance degrades THEN the Callback_Forwarder SHALL provide metrics for monitoring

### Requirement 7

**User Story:** As a system operator, I want comprehensive logging and monitoring, so that I can track event ingestion health.

#### Acceptance Criteria

1. WHEN events are received THEN the Callback_Forwarder SHALL log event metadata
2. WHEN errors occur THEN the Callback_Forwarder SHALL log detailed error information
3. WHEN performance metrics are needed THEN the Callback_Forwarder SHALL expose health endpoints
4. WHEN monitoring systems query status THEN the Callback_Forwarder SHALL provide ingestion rates
5. WHEN logs are generated THEN the Callback_Forwarder SHALL use structured JSON logging

### Requirement 8

**User Story:** As a deployment engineer, I want the service to be easily deployable, so that it can run alongside other components.

#### Acceptance Criteria

1. WHEN deploying the service THEN the Callback_Forwarder SHALL use the same PostgreSQL database
2. WHEN configuration is needed THEN the Callback_Forwarder SHALL use environment variables
3. WHEN health checks are performed THEN the Callback_Forwarder SHALL provide /health endpoint
4. WHEN the service starts THEN the Callback_Forwarder SHALL verify database connectivity
5. WHEN deployment occurs THEN the Callback_Forwarder SHALL support Railway deployment

### Requirement 9

**User Story:** As a data engineer, I want event storage to be efficient and queryable, so that downstream processing can access events effectively.

#### Acceptance Criteria

1. WHEN storing events THEN the Callback_Forwarder SHALL use appropriate database indexes
2. WHEN events are queried THEN the Callback_Forwarder SHALL support efficient lookups by timestamp
3. WHEN event data is stored THEN the Callback_Forwarder SHALL use JSONB for flexible querying
4. WHEN database schema is designed THEN the Callback_Forwarder SHALL optimize for write performance
5. WHEN data retention is considered THEN the Callback_Forwarder SHALL support event archiving

### Requirement 10

**User Story:** As a system integrator, I want error handling to be robust, so that temporary issues don't cause event loss.

#### Acceptance Criteria

1. WHEN database connections fail THEN the Callback_Forwarder SHALL retry with exponential backoff
2. WHEN transient errors occur THEN the Callback_Forwarder SHALL distinguish them from permanent failures
3. WHEN retry limits are reached THEN the Callback_Forwarder SHALL return appropriate error codes
4. WHEN errors are logged THEN the Callback_Forwarder SHALL include correlation IDs
5. WHEN system recovery occurs THEN the Callback_Forwarder SHALL resume normal operation automatically
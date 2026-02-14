# Callback Forwarder Service - Release Notes

## v0.1.0 - Initial Ingestion Release (2026-02-14)

### Overview
- First deployed version of the Callback Forwarder Service for capturing Alchemy webhooks and persisting them to Railway Postgres in the `forwarder` schema.

### Endpoints
- `POST /webhook/alchemy`
  - Accepts raw JSON webhook payloads (e.g., Alchemy Address Activity).
  - Logs full payload and basic metadata.
  - Runs through:
    - `IWebhookAuthenticationService` (fail-open by default; can be enabled via `CALLBACK_FORWARDER_ENABLE_AUTH=true`).
    - `IDuplicateDetectionService` (SHA256-based content hash, in-memory cache, and DB check).
    - `IEventRepository` (EF Core + Npgsql, writes to `forwarder.raw_webhook_events`).
  - Responses:
    - `200` with `{ message: "Event stored", eventId, duplicate: false }` for new events.
    - `200` with `{ message: "Event already processed", duplicate: true }` for detected duplicates.
    - `401` when authentication is explicitly enabled and fails.
    - `500` on unexpected persistence errors.

- `GET /webhook/alchemy/events`
  - Returns up to 50 most recent stored events ordered by `ReceivedAt` (descending).
  - Projects `SourceIp` to string for serialization; response includes `Id`, `Provider`, `EventType`, `EventData`, `EventHash`, `ReceivedAt`, `SourceIp`, and `Headers`.
  - Intended for debugging and manual verification via Swagger.

- `GET /ping`
  - Simple health probe returning `"pong"`.

### Swagger / Manual Testing
- Swagger UI available at `/swagger` when `ENABLE_SWAGGER=true` or in Development.
- `POST /webhook/alchemy` is documented with an `application/json` request body so Alchemy-style payloads can be pasted directly.
- `GET /webhook/alchemy/events` exposed for reading back stored events without direct DB access.

### Persistence & Database
- Uses Railway `DATABASE_URL` (or `ConnectionStrings:Database`) with EF Core 8 + Npgsql.
- `CallbackForwarderDbContext` configured with default schema `forwarder` and migrations history table `forwarder.__EFMigrationsHistoryForwarder`.
- Table `forwarder.raw_webhook_events` includes JSONB columns (`EventData`, `Headers`) and indexes on:
  - `ReceivedAt` (DESC) for recent queries.
  - `Provider` and `EventType` for filtering.
  - `(Provider, EventHash)` for duplicate detection.
- Startup migration runner (gated by `RUN_MIGRATIONS_ON_STARTUP=true`) applies migrations and falls back to table creation if necessary without failing the app.

### Core Components
- Domain models: `IncomingWebhookEvent`, `RawWebhookEvent`, `EventProcessingResult`, `AuthenticationResult`, `HealthStatus`, `IngestionMetrics`.
- Persistence:
  - `IEventRepository` / `EventRepository` with retry policy (`RetryPolicy`) using exponential backoff for transient DB errors.
- Duplicate detection:
  - `IDuplicateDetectionService` / `DuplicateDetectionService` using SHA256 over JSON payload, a 5-minute in-memory cache, and DB fallback.
- Authentication (implemented, disabled by default):
  - `IWebhookAuthenticationService` / `WebhookAuthenticationService`.
  - Environment/configuration keys:
    - `CALLBACK_FORWARDER_ENABLE_AUTH` – enable/disable auth (default: false).
    - `CALLBACK_FORWARDER_SECRET_<PROVIDER>` and/or `CallbackForwarder:Authentication:ProviderSecrets:{provider}` – per-provider HMAC secrets.
    - `CALLBACK_FORWARDER_ALLOWED_IPS` – optional comma-separated IP allowlist.
  - If auth is enabled but no secret is configured, requests are still accepted (fail-open) with a warning log.

### Deployment
- Containerized via Docker and deployed to Railway.
- Binds to `PORT` with `ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}`.
- Console logging enabled; Swagger used for live endpoint exploration and manual tests.

### Notes
- Security features (auth, IP allowlist) are present but opt-in; current deployment is focused on reliable ingestion and observability rather than strict verification.
- Future releases may add controller-based API, structured logging with Serilog, richer health endpoints, and configuration options per the design spec.

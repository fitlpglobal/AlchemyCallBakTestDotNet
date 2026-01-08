# Guardrails — Callback Forwarder Service (AUTHORITATIVE)

This service is ALREADY LIVE on Railway and is integrated with:
- Existing Alchemy webhooks
- A shared PostgreSQL database used by other gateway services

These guardrails are NON-NEGOTIABLE.
They exist to prevent breaking live payment detection.
Copilot and contributors MUST follow all rules below.

================================================================

1. BACKWARD COMPATIBILITY — DO NOT BREAK LIVE WEBHOOKS

The service MUST continue to support the following endpoint:

POST /webhook/alchemy

This endpoint is already registered in Alchemy.
It MUST NOT be removed, renamed, or changed.
It MUST return HTTP 200 OK after successfully persisting the webhook payload.

New endpoints may be added, but this endpoint MUST always remain functional.

Rule of thumb:
If the Alchemy Dashboard does not need updating, the change is safe.

================================================================

2. WEBHOOK AUTHENTICATION — FAIL OPEN BY DEFAULT

Webhook signature verification MAY be implemented, but it MUST NOT block ingestion by default.

If a signature is missing or invalid:
- Log a warning
- Persist the event
- Return HTTP 200 OK

Events MUST NOT be rejected unless the request is unreadable
(e.g. empty body or invalid JSON).

Any fail-closed authentication behavior MUST be gated behind an explicit feature flag
(e.g. STRICT_WEBHOOK_AUTH=true).

Principle:
Store first. Validate later. Never lose money-related events.

================================================================

3. SHARED DATABASE OWNERSHIP — CRITICAL

This service uses the SAME PostgreSQL instance as other gateway services.
It ONLY owns its own tables, preferably in a dedicated schema (e.g. "forwarder").

This service MUST NOT:
- Scaffold the full database
- Touch PaymentMethods tables
- Touch Invoicing tables
- Share the same EF migrations history table as other services

EF Core MUST:
- Use a dedicated schema (e.g. "forwarder")
- Use a dedicated migrations history table

Principle:
Shared database does NOT mean shared ownership.

================================================================

4. DEDUPLICATION & DATA SAFETY — ERR ON STORING

Duplicate webhook events WILL happen.
Deduplication MUST err on the side of storing events, not rejecting them.

Rules:
- Prefer provider-supplied event IDs when available
- Otherwise, use content hashing as a fallback
- DO NOT use globally aggressive unique constraints

Recommended uniqueness constraint:
(provider, event_hash)

If unsure:
Store the event and let downstream systems decide.

================================================================

FINAL CONSTRAINT — DO NOT VIOLATE

This service is a DUMB INGESTION LAYER.

It DOES:
- Receive webhooks
- Persist raw payloads
- Return fast HTTP 200 OK responses

It MUST NOT:
- Interpret events
- Update invoices
- Call external APIs
- Contain business logic

Any deviation from these guardrails requires explicit architectural approval.

# Copilot Instructions: Alchemy Callback Test (.NET 8)

## Overview
- **Purpose:** Minimal ASP.NET Core API to receive Alchemy webhooks and log payloads.
- **Primary endpoints:**
  - POST `/webhook/alchemy` — reads raw body, attempts JSON deserialization, logs.
  - GET `/ping` — simple health check.
- **Active project:** Changes should be made in [AlchemyCallbackTest/Program.cs](AlchemyCallbackTest/Program.cs). The root-level [Program.cs](Program.cs) is a duplicate and not part of the build.

## Architecture & Components
- **Minimal API:** Single-file endpoint definitions in [AlchemyCallbackTest/Program.cs](AlchemyCallbackTest/Program.cs).
- **Models:** Local record types (`WebhookPayload`, `EventData`) declared at the bottom of [AlchemyCallbackTest/Program.cs](AlchemyCallbackTest/Program.cs) and used for `System.Text.Json` deserialization.
- **Configuration:**
  - App settings in [AlchemyCallbackTest/appsettings.json](AlchemyCallbackTest/appsettings.json) and [AlchemyCallbackTest/appsettings.Development.json](AlchemyCallbackTest/appsettings.Development.json).
  - Launch profiles/ports in [AlchemyCallbackTest/Properties/launchSettings.json](AlchemyCallbackTest/Properties/launchSettings.json).
- **Solution/Project:** Solution root has [AlchemyCallBakTestDotNet.sln](AlchemyCallBakTestDotNet.sln); the web project is [AlchemyCallbackTest/AlchemyCallbackTest.csproj](AlchemyCallbackTest/AlchemyCallbackTest.csproj).

## Endpoint Behavior
- **`POST /webhook/alchemy`:**
  - Reads raw request body via `StreamReader` and logs it.
  - Attempts `JsonSerializer.Deserialize<WebhookPayload>`; logs selected fields if present.
  - Returns `200 OK` regardless of deserialization outcome; exceptions during deserialization are logged to console.
- **`GET /ping`:** returns `"pong"`.

## Build & Run
- **Local dev (Windows):** from the project folder:
  - `cd AlchemyCallbackTest`
  - `dotnet run`
  - Default HTTP profile uses `http://localhost:5273` (see [launchSettings.json](AlchemyCallbackTest/Properties/launchSettings.json)).
- **Custom port:**
  - `dotnet run --urls=http://0.0.0.0:8080`
  - The repo also provides [start.sh](start.sh) (Linux/macOS) which runs on `${PORT:-8080}`.
- **Docker:** see [Dockerfile](Dockerfile). It publishes the project and runs the app with `ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}`.

## Logging
- **Console only:** Providers are cleared and console logging is added in [AlchemyCallbackTest/Program.cs](AlchemyCallbackTest/Program.cs).
- **Log levels:** Controlled via appsettings (Default `Information`, `Microsoft.AspNetCore` `Warning`).

## Conventions & Patterns
- **Minimal endpoints in `Program.cs`:** Use `app.MapPost`/`MapGet` for new routes.
- **Raw body handling:** For webhook-style inputs, read `HttpRequest.Body` once and deserialize with `System.Text.Json`.
- **Record models:** Keep lightweight records near the endpoint unless models grow in complexity.
- **Response policy:** Return `Results.Ok()` for webhook ACK unless the integration requires otherwise.
- **No DI/services yet:** There is no custom service registration; add `builder.Services` registrations when introducing new components.

## Integration Notes
- **Expected payload fields:** The code references `WebhookId`, `Type`, and `Event.Network` if present.
- **Schema flexibility:** Deserialization is best-effort; missing fields will log a deserialization error but still return `200`.

## Examples
- **Add a new endpoint:**
  - In [AlchemyCallbackTest/Program.cs](AlchemyCallbackTest/Program.cs):
    - `app.MapPost("/webhook/other", async (HttpRequest request) => { /* read + log */ return Results.Ok(); });`
- **Extend payload:**
  - Update the record types at the bottom of [AlchemyCallbackTest/Program.cs](AlchemyCallbackTest/Program.cs) to reflect additional fields observed in Alchemy payloads.

## Where to Change
- **Active code:** [AlchemyCallbackTest/Program.cs](AlchemyCallbackTest/Program.cs)
- **Project metadata:** [AlchemyCallbackTest/AlchemyCallbackTest.csproj](AlchemyCallbackTest/AlchemyCallbackTest.csproj)
- **Runtime config:** [AlchemyCallbackTest/appsettings.json](AlchemyCallbackTest/appsettings.json), [AlchemyCallbackTest/appsettings.Development.json](AlchemyCallbackTest/appsettings.Development.json), [AlchemyCallbackTest/Properties/launchSettings.json](AlchemyCallbackTest/Properties/launchSettings.json)
- **Containerization:** [Dockerfile](Dockerfile)

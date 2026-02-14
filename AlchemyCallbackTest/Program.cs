using System.Text;
using System.Text.Json;
using System.Linq;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AlchemyCallbackTest.Forwarder;
using AlchemyCallbackTest.Persistence;
using AlchemyCallbackTest.Services;
using AlchemyCallbackTest.Domain;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Enable console logging only
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Swagger/OpenAPI (Step 0): services registration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Callback Forwarder Service",
        Version = "v1",
        Description = "Webhook ingestion endpoints for blockchain providers"
    });
});

// Step 1: Database setup (EF Core + Npgsql) with forwarder schema
string? connectionString = builder.Configuration.GetConnectionString("Database");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var databaseUrl = builder.Configuration["DATABASE_URL"];
    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var dbName = uri.AbsolutePath.TrimStart('/');

        var npgsqlBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port,
            Username = username,
            Password = password,
            Database = dbName,
            SslMode = SslMode.Require,
            TrustServerCertificate = true,
            Pooling = true
        };
        connectionString = npgsqlBuilder.ToString();
    }
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Warning: No database connection string configured. Set ConnectionStrings__Database or DATABASE_URL.");
}

builder.Services.AddDbContext<CallbackForwarderDbContext>(options =>
{
    options.UseNpgsql(connectionString ?? string.Empty, x =>
    {
        x.MigrationsHistoryTable("__EFMigrationsHistoryForwarder", "forwarder");
    });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

// Persistence layer registrations (repository + retry policy)
builder.Services.AddScoped<IRetryPolicy, RetryPolicy>();
builder.Services.AddScoped<IEventRepository, EventRepository>();

// Duplicate detection service
builder.Services.AddSingleton<IDuplicateDetectionService, DuplicateDetectionService>();

// Webhook authentication service (fail-open until explicitly enabled via env)
builder.Services.AddSingleton<IWebhookAuthenticationService, WebhookAuthenticationService>();

var app = builder.Build();

// Swagger/OpenAPI (Step 0): gated by ENABLE_SWAGGER or Development environment
var enableSwagger = false;
var enableSwaggerValue = builder.Configuration["ENABLE_SWAGGER"];
if (!string.IsNullOrWhiteSpace(enableSwaggerValue) && bool.TryParse(enableSwaggerValue, out var parsed))
{
    enableSwagger = parsed;
}
else if (app.Environment.IsDevelopment())
{
    enableSwagger = true;
}

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Callback Forwarder API v1");
        c.RoutePrefix = "swagger"; // UI at /swagger
    });
    // Convenience: redirect root to Swagger UI when enabled
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

// Step 2: Optional startup migrations (gated)
var runMigrationsValue = builder.Configuration["RUN_MIGRATIONS_ON_STARTUP"];
if (!string.IsNullOrWhiteSpace(runMigrationsValue) && bool.TryParse(runMigrationsValue, out var runMigrations) && runMigrations)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CallbackForwarderDbContext>();
    try
    {
        Console.WriteLine("Applying forwarder migrations at startup...");
        db.Database.Migrate();
        Console.WriteLine("Forwarder migrations applied successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Startup migration failed: {ex.Message}");
        // Continue running per guardrails; do not crash ingestion
    }

    // Fallback: if the forwarder table still doesn't exist, create tables from the current model
    try
    {
        var exists = false;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='forwarder' AND c.relname='raw_webhook_events')",
                conn);
            var result = await cmd.ExecuteScalarAsync();
            exists = result is bool b && b;
        }

        if (!exists)
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();
            creator.CreateTables();
            Console.WriteLine("Forwarder tables created via EnsureCreated fallback.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Fallback table creation failed: {ex.Message}");
    }
}

// Minimal API: POST /webhook/alchemy
var alchemyWebhook = app.MapPost("/webhook/alchemy", async (
    HttpRequest request,
    IEventRepository eventRepository,
    IDuplicateDetectionService duplicateDetectionService,
    IWebhookAuthenticationService authenticationService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("WebhookAlchemy");

    // Read raw body as string
    string body;
    using (var reader = new StreamReader(request.Body, Encoding.UTF8))
    {
        body = await reader.ReadToEndAsync(cancellationToken);
    }
    logger.LogInformation("Alchemy webhook received: {Body}", body);

    WebhookPayload? payload = null;
    try
    {
        payload = JsonSerializer.Deserialize<WebhookPayload>(body);
        if (payload != null)
        {
            logger.LogInformation("webhookId: {WebhookId}, type: {Type}, network: {Network}",
                payload.WebhookId, payload.Type, payload.Event?.Network);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Deserialization error for Alchemy webhook.");
    }

    var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

    var signature =
        request.Headers["X-Alchemy-Signature"].FirstOrDefault()
        ?? request.Headers["X-Signature"].FirstOrDefault()
        ?? request.Headers["X-Hub-Signature-256"].FirstOrDefault();

    var incomingEvent = new IncomingWebhookEvent
    {
        Provider = "alchemy",
        EventType = payload?.Type ?? "unknown",
        EventData = body,
        Signature = string.IsNullOrWhiteSpace(signature) ? null : signature,
        SourceIp = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
        ReceivedAt = DateTimeOffset.UtcNow,
        Headers = headers
    };

    var authResult = await authenticationService.AuthenticateAsync(incomingEvent, cancellationToken);
    if (!authResult.IsAuthenticated)
    {
        logger.LogWarning("Webhook authentication failed for provider {Provider}: {Reason}", incomingEvent.Provider, authResult.FailureReason);
        return Results.Unauthorized();
    }

    var isDuplicate = await duplicateDetectionService.IsDuplicateAsync(incomingEvent.Provider, incomingEvent.EventData, cancellationToken);
    if (isDuplicate)
    {
        logger.LogInformation("Duplicate webhook event detected for provider {Provider}.", incomingEvent.Provider);
        return Results.Ok(new
        {
            message = "Event already processed",
            duplicate = true
        });
    }

    var rawEvent = new RawWebhookEvent
    {
        Provider = incomingEvent.Provider,
        EventType = incomingEvent.EventType,
        EventData = incomingEvent.EventData,
        EventHash = duplicateDetectionService.ComputeEventHash(incomingEvent.EventData),
        ReceivedAt = incomingEvent.ReceivedAt,
        SourceIp = incomingEvent.SourceIp,
        Headers = incomingEvent.Headers
    };

    string eventId;
    try
    {
        eventId = await eventRepository.StoreEventAsync(rawEvent, cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to store webhook event from provider {Provider}.", incomingEvent.Provider);
        return Results.StatusCode(500);
    }

    return Results.Ok(new
    {
        message = "Event stored",
        eventId,
        duplicate = false
    });
});

// Hint to Swagger/OpenAPI: this endpoint accepts a JSON request body
alchemyWebhook.Accepts<string>("application/json");

// Debug endpoint: read back recent stored events
app.MapGet("/webhook/alchemy/events", async (CallbackForwarderDbContext db, CancellationToken cancellationToken) =>
{
    var events = await db.RawWebhookEvents
        .OrderByDescending(e => e.ReceivedAt)
        .Take(50)
        .ToListAsync(cancellationToken);

    return Results.Ok(events);
});

// Minimal API: GET /ping
app.MapGet("/ping", () => "pong");

app.Run();

// Record types
public record WebhookPayload(string WebhookId, string Type, EventData Event);
public record EventData(string Network);

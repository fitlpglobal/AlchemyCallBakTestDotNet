using System.Text;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AlchemyCallbackTest.Forwarder;
using Microsoft.EntityFrameworkCore.Storage;

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
            var creator = db.Database.GetService<IRelationalDatabaseCreator>();
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
app.MapPost("/webhook/alchemy", async (HttpRequest request) =>
{
    // Read raw body as string
    string body;
    using (var reader = new StreamReader(request.Body, Encoding.UTF8))
    {
        body = await reader.ReadToEndAsync();
    }
    Console.WriteLine($"Alchemy webhook: {body}");

    // Try to deserialize
    try
    {
        var payload = JsonSerializer.Deserialize<WebhookPayload>(body);
        if (payload != null)
        {
            Console.WriteLine($"webhookId: {payload.WebhookId}, type: {payload.Type}, network: {payload.Event?.Network}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Deserialization error: {ex.Message}");
    }
    return Results.Ok();
});

// Minimal API: GET /ping
app.MapGet("/ping", () => "pong");

app.Run();

// Record types
public record WebhookPayload(string WebhookId, string Type, EventData Event);
public record EventData(string Network);

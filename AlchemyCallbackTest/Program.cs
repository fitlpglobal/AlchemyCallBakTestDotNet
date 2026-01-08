using System.Text;
using System.Text.Json;
using Microsoft.OpenApi.Models;

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

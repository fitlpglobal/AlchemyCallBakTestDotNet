using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Enable console logging only
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

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

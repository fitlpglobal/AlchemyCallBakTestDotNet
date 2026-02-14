using System;
using System.Collections.Generic;

namespace AlchemyCallbackTest.Domain
{
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
}

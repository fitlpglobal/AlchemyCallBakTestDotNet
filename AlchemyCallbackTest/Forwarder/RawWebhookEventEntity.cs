using System;
using System.Net;

namespace AlchemyCallbackTest.Forwarder
{
    public sealed class RawWebhookEventEntity
    {
        public Guid Id { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string EventData { get; set; } = string.Empty;
        public string EventHash { get; set; } = string.Empty;
        public DateTimeOffset ReceivedAt { get; set; }
        public IPAddress? SourceIp { get; set; }
        public string? Headers { get; set; }
    }
}

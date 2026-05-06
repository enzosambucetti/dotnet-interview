using System.Text.Json.Serialization;

namespace TodoApi.Realtime;

public class TodoRealtimeEvent
{
    public required string EventType { get; set; }
    public required string EntityType { get; set; }
    public long? EntityId { get; set; }
    public long? TodoListId { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public object? Payload { get; set; }

    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; set; }
}

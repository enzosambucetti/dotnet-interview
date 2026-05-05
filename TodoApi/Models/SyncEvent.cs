namespace TodoApi.Models;

public class SyncEvent
{
    public long Id { get; set; }
    public required string EntityType { get; set; }
    public long EntityId { get; set; }
    public required string EventType { get; set; }
    public required string PayloadJson { get; set; }
    public required string Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public required string CorrelationId { get; set; }
}

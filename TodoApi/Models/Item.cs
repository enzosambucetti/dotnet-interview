using System.Text.Json.Serialization;

namespace TodoApi.Models;

public class Item
{
    public long Id { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    public required string Name { get; set; }
    public bool IsCompleted { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("is_deleted")]
    public bool IsDeleted { get; set; }

    [JsonPropertyName("deleted_at")]
    public DateTimeOffset? DeletedAt { get; set; }

    public long TodoListId { get; set; }

    [JsonIgnore]
    public TodoList TodoList { get; set; } = default!;
}

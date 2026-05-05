using System.Text.Json.Serialization;

namespace TodoApi.Models;

public class TodoList
{
    public long Id { get; set; }
    
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    public required string Name { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("is_deleted")]
    public bool IsDeleted { get; set; }

    [JsonPropertyName("deleted_at")]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonIgnore]
    public ICollection<Item> Items { get; set; } = new List<Item>();
}

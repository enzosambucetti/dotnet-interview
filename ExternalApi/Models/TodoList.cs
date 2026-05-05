using System.Text.Json.Serialization;

namespace ExternalApi.Models;

public class TodoList
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("items")]
    public IList<TodoItem> Items { get; set; } = new List<TodoItem>();
}

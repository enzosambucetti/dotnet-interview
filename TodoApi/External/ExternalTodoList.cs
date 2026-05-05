using System.Text.Json.Serialization;

namespace TodoApi.External;

public class ExternalTodoList
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("items")]
    public IList<ExternalTodoItem> Items { get; set; } = new List<ExternalTodoItem>();
}

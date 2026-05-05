using System.Text.Json.Serialization;

namespace TodoApi.Dtos;

public class CreateItem
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    public required string Name { get; set; }
    public bool IsCompleted { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

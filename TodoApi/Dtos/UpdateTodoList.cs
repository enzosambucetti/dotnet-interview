using System.Text.Json.Serialization;

namespace TodoApi.Dtos;

public class UpdateTodoList
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    public required string Name { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

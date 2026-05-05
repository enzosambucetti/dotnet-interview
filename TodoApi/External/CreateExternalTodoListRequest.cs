using System.Text.Json.Serialization;

namespace TodoApi.External;

public class CreateExternalTodoListRequest
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("items")]
    public IList<CreateExternalTodoItemRequest>? Items { get; set; }
}

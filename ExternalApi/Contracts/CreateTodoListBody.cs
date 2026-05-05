using System.Text.Json.Serialization;

namespace ExternalApi.Contracts;

public class CreateTodoListBody
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("items")]
    public IList<CreateTodoItemBody>? Items { get; set; }
}

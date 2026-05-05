using System.Text.Json.Serialization;

namespace ExternalApi.Contracts;

public class UpdateTodoItemBody
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("completed")]
    public bool? Completed { get; set; }
}

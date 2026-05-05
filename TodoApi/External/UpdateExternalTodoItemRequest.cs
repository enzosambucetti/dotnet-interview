using System.Text.Json.Serialization;

namespace TodoApi.External;

public class UpdateExternalTodoItemRequest
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }
}

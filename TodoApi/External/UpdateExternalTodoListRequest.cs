using System.Text.Json.Serialization;

namespace TodoApi.External;

public class UpdateExternalTodoListRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

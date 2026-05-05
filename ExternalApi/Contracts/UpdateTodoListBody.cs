using System.Text.Json.Serialization;

namespace ExternalApi.Contracts;

public class UpdateTodoListBody
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

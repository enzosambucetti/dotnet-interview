namespace TodoApi.External;

public class ExternalApiOptions
{
    public const string SectionName = "ExternalApi";

    public required string BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TodoApi.External;

public class ExternalTodoApiClient : IExternalTodoApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalTodoApiClient> _logger;
    private readonly ExternalApiOptions _options;

    public ExternalTodoApiClient(
        HttpClient httpClient,
        IOptions<ExternalApiOptions> options,
        ILogger<ExternalTodoApiClient> logger
    )
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IList<ExternalTodoList>> ListTodoListsAsync(CancellationToken cancellationToken)
    {
        return await SendAsync(
            () => _httpClient.GetAsync("/todolists", cancellationToken),
            async response =>
                await response.Content.ReadFromJsonAsync<IList<ExternalTodoList>>(
                    JsonOptions,
                    cancellationToken
                ) ?? new List<ExternalTodoList>(),
            cancellationToken
        );
    }

    public Task<ExternalTodoList> CreateTodoListAsync(
        CreateExternalTodoListRequest request,
        CancellationToken cancellationToken
    )
    {
        return SendAsync(
            () => _httpClient.PostAsJsonAsync("/todolists", request, JsonOptions, cancellationToken),
            response => ReadRequiredAsync<ExternalTodoList>(response, cancellationToken),
            cancellationToken
        );
    }

    public Task<ExternalTodoList> UpdateTodoListAsync(
        string todoListId,
        UpdateExternalTodoListRequest request,
        CancellationToken cancellationToken
    )
    {
        return SendAsync(
            () =>
                _httpClient.PatchAsJsonAsync(
                    $"/todolists/{Uri.EscapeDataString(todoListId)}",
                    request,
                    JsonOptions,
                    cancellationToken
                ),
            response => ReadRequiredAsync<ExternalTodoList>(response, cancellationToken),
            cancellationToken
        );
    }

    public Task DeleteTodoListAsync(string todoListId, CancellationToken cancellationToken)
    {
        return SendAsync(
            () => _httpClient.DeleteAsync($"/todolists/{Uri.EscapeDataString(todoListId)}", cancellationToken),
            _ => Task.FromResult(true),
            cancellationToken
        );
    }

    public Task<ExternalTodoItem> CreateTodoItemAsync(
        string todoListId,
        CreateExternalTodoItemRequest request,
        CancellationToken cancellationToken
    )
    {
        return SendAsync(
            () =>
                _httpClient.PostAsJsonAsync(
                    $"/todolists/{Uri.EscapeDataString(todoListId)}/todoitems",
                    request,
                    JsonOptions,
                    cancellationToken
                ),
            response => ReadRequiredAsync<ExternalTodoItem>(response, cancellationToken),
            cancellationToken
        );
    }

    public Task<ExternalTodoItem> UpdateTodoItemAsync(
        string todoListId,
        string todoItemId,
        UpdateExternalTodoItemRequest request,
        CancellationToken cancellationToken
    )
    {
        return SendAsync(
            () =>
                _httpClient.PatchAsJsonAsync(
                    $"/todolists/{Uri.EscapeDataString(todoListId)}/todoitems/{Uri.EscapeDataString(todoItemId)}",
                    request,
                    JsonOptions,
                    cancellationToken
                ),
            response => ReadRequiredAsync<ExternalTodoItem>(response, cancellationToken),
            cancellationToken
        );
    }

    public Task DeleteTodoItemAsync(
        string todoListId,
        string todoItemId,
        CancellationToken cancellationToken
    )
    {
        return SendAsync(
            () =>
                _httpClient.DeleteAsync(
                    $"/todolists/{Uri.EscapeDataString(todoListId)}/todoitems/{Uri.EscapeDataString(todoItemId)}",
                    cancellationToken
                ),
            _ => Task.FromResult(true),
            cancellationToken
        );
    }

    private async Task<T> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, Task<T>> read,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await send();

                if (response.IsSuccessStatusCode)
                {
                    return await read(response);
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new ExternalApiException(
                    response.StatusCode,
                    $"External API returned {(int)response.StatusCode} {response.StatusCode}. Body: {body}"
                );
            }
            catch (Exception exception) when (
                attempt < _options.MaxRetries
                && exception is not ExternalApiException { StatusCode: HttpStatusCode.NotFound }
            )
            {
                _logger.LogWarning(
                    exception,
                    "External API call failed. Attempt {Attempt}/{MaxRetries}",
                    attempt,
                    _options.MaxRetries
                );

                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);

        if (value == null)
        {
            throw new InvalidOperationException($"External API returned an empty {typeof(T).Name} response.");
        }

        return value;
    }
}

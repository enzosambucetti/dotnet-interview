namespace TodoApi.External;

public interface IExternalTodoApiClient
{
    Task<IList<ExternalTodoList>> ListTodoListsAsync(CancellationToken cancellationToken);
    Task<ExternalTodoList> CreateTodoListAsync(
        CreateExternalTodoListRequest request,
        CancellationToken cancellationToken
    );
    Task<ExternalTodoList> UpdateTodoListAsync(
        string todoListId,
        UpdateExternalTodoListRequest request,
        CancellationToken cancellationToken
    );
    Task DeleteTodoListAsync(string todoListId, CancellationToken cancellationToken);
    Task<ExternalTodoItem> CreateTodoItemAsync(
        string todoListId,
        CreateExternalTodoItemRequest request,
        CancellationToken cancellationToken
    );
    Task<ExternalTodoItem> UpdateTodoItemAsync(
        string todoListId,
        string todoItemId,
        UpdateExternalTodoItemRequest request,
        CancellationToken cancellationToken
    );
    Task DeleteTodoItemAsync(
        string todoListId,
        string todoItemId,
        CancellationToken cancellationToken
    );
}

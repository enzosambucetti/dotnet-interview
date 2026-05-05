using Microsoft.Extensions.Logging;

namespace TodoApi.Errors;

public static class ApiErrorEventIds
{
    public static readonly EventId UnhandledHttpException = new(1000, nameof(UnhandledHttpException));
    public static readonly EventId TodoListNotFound = new(1404, nameof(TodoListNotFound));
    public static readonly EventId TodoItemNotFound = new(2404, nameof(TodoItemNotFound));
}

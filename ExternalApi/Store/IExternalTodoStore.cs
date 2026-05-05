using ExternalApi.Contracts;
using ExternalApi.Models;

namespace ExternalApi.Store;

public interface IExternalTodoStore
{
    IList<TodoList> GetTodoLists();
    TodoList CreateTodoList(CreateTodoListBody body);
    TodoList? UpdateTodoList(string todoListId, UpdateTodoListBody body);
    bool DeleteTodoList(string todoListId);
    TodoItem? UpdateTodoItem(string todoListId, string todoItemId, UpdateTodoItemBody body);
    bool DeleteTodoItem(string todoListId, string todoItemId);
    void Reset();
}

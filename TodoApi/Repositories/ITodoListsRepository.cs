using TodoApi.Models;

namespace TodoApi.Repositories;

public interface ITodoListsRepository
{
    Task<IList<TodoList>> GetTodoListsAsync();
    Task<TodoList?> GetTodoListAsync(long id);
    Task<TodoList?> GetTodoListWithItemsAsync(long id);
    Task<TodoList> AddTodoListAsync(TodoList todoList);
    Task<TodoList> UpdateTodoListAsync(TodoList todoList);
    Task SoftDeleteTodoListAsync(TodoList todoList, DateTimeOffset deletedAt);
}

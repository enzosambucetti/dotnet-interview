using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Services;

public interface ITodoListsService
{
    Task<IList<TodoList>> GetTodoListsAsync();
    Task<TodoList?> GetTodoListAsync(long id);
    Task<TodoList> CreateTodoListAsync(CreateTodoList payload);
    Task<TodoList?> UpdateTodoListAsync(long id, UpdateTodoList payload);
    Task<bool> DeleteTodoListAsync(long id);
}

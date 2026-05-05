using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;

namespace TodoApi.Services;

public class TodoListsService : ITodoListsService
{
    private readonly ITodoListsRepository _todoListsRepository;

    public TodoListsService(ITodoListsRepository todoListsRepository)
    {
        _todoListsRepository = todoListsRepository;
    }

    public Task<IList<TodoList>> GetTodoListsAsync()
    {
        return _todoListsRepository.GetTodoListsAsync();
    }

    public Task<TodoList?> GetTodoListAsync(long id)
    {
        return _todoListsRepository.GetTodoListAsync(id);
    }

    public Task<TodoList> CreateTodoListAsync(CreateTodoList payload)
    {
        var createdAt = payload.CreatedAt ?? DateTimeOffset.UtcNow;
        var todoList = new TodoList
        {
            SourceId = payload.SourceId,
            Name = payload.Name,
            CreatedAt = createdAt,
            UpdatedAt = payload.UpdatedAt ?? createdAt,
        };

        return _todoListsRepository.AddTodoListAsync(todoList);
    }

    public async Task<TodoList?> UpdateTodoListAsync(long id, UpdateTodoList payload)
    {
        var todoList = await _todoListsRepository.GetTodoListAsync(id);

        if (todoList == null)
        {
            return null;
        }

        if (payload.SourceId != null)
        {
            todoList.SourceId = payload.SourceId;
        }

        todoList.Name = payload.Name;
        todoList.UpdatedAt = payload.UpdatedAt ?? DateTimeOffset.UtcNow;

        return await _todoListsRepository.UpdateTodoListAsync(todoList);
    }

    public async Task<bool> DeleteTodoListAsync(long id)
    {
        var todoList = await _todoListsRepository.GetTodoListAsync(id);

        if (todoList == null)
        {
            return false;
        }

        await _todoListsRepository.SoftDeleteTodoListAsync(todoList, DateTimeOffset.UtcNow);
        return true;
    }
}

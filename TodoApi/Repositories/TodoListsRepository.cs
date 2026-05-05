using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Repositories;

public class TodoListsRepository : ITodoListsRepository
{
    private readonly TodoContext _context;

    public TodoListsRepository(TodoContext context)
    {
        _context = context;
    }

    public async Task<IList<TodoList>> GetTodoListsAsync()
    {
        return await _context.TodoList.ToListAsync();
    }

    public Task<TodoList?> GetTodoListAsync(long id)
    {
        return _context.TodoList.FirstOrDefaultAsync(todoList => todoList.Id == id);
    }

    public async Task<TodoList> AddTodoListAsync(TodoList todoList)
    {
        _context.TodoList.Add(todoList);
        await _context.SaveChangesAsync();

        return todoList;
    }

    public async Task<TodoList> UpdateTodoListAsync(TodoList todoList)
    {
        await _context.SaveChangesAsync();

        return todoList;
    }

    public async Task SoftDeleteTodoListAsync(TodoList todoList, DateTimeOffset deletedAt)
    {
        todoList.IsDeleted = true;
        todoList.DeletedAt = deletedAt;
        todoList.UpdatedAt = deletedAt;

        var items = await _context.Items
            .Where(item => item.TodoListId == todoList.Id)
            .ToListAsync();

        foreach (var item in items)
        {
            item.IsDeleted = true;
            item.DeletedAt = deletedAt;
            item.UpdatedAt = deletedAt;
        }

        await _context.SaveChangesAsync();
    }
}

using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Repositories;

public class ItemsRepository : IItemsRepository
{
    private readonly TodoContext _context;

    public ItemsRepository(TodoContext context)
    {
        _context = context;
    }

    public Task<bool> TodoListExistsAsync(long todoListId)
    {
        return _context.TodoList.AnyAsync(todoList => todoList.Id == todoListId);
    }

    public async Task<IList<Item>> GetItemsAsync(long todoListId)
    {
        return await _context.Items
            .Where(item => item.TodoListId == todoListId)
            .ToListAsync();
    }

    public Task<Item?> GetItemAsync(long todoListId, long id)
    {
        return _context.Items
            .FirstOrDefaultAsync(item => item.TodoListId == todoListId && item.Id == id);
    }

    public async Task<Item> AddItemAsync(Item item)
    {
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        return item;
    }

    public async Task<Item> UpdateItemAsync(Item item)
    {
        await _context.SaveChangesAsync();

        return item;
    }

    public async Task SoftDeleteItemAsync(Item item, DateTimeOffset deletedAt)
    {
        item.IsDeleted = true;
        item.DeletedAt = deletedAt;
        item.UpdatedAt = deletedAt;

        await _context.SaveChangesAsync();
    }
}

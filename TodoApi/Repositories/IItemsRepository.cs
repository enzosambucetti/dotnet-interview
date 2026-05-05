using TodoApi.Models;

namespace TodoApi.Repositories;

public interface IItemsRepository
{
    Task<bool> TodoListExistsAsync(long todoListId);
    Task<IList<Item>> GetItemsAsync(long todoListId);
    Task<Item?> GetItemAsync(long todoListId, long id);
    Task<Item> AddItemAsync(Item item);
    Task<Item> UpdateItemAsync(Item item);
    Task SoftDeleteItemAsync(Item item, DateTimeOffset deletedAt);
}

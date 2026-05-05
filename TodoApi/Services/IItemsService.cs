using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Services;

public interface IItemsService
{
    Task<IList<Item>?> GetItemsAsync(long todoListId);
    Task<Item?> GetItemAsync(long todoListId, long id);
    Task<Item?> CreateItemAsync(long todoListId, CreateItem payload);
    Task<Item?> UpdateItemAsync(long todoListId, long id, UpdateItem payload);
    Task<bool> DeleteItemAsync(long todoListId, long id);
}

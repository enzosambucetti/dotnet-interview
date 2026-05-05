using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;

namespace TodoApi.Services;

public class ItemsService : IItemsService
{
    private readonly IItemsRepository _itemsRepository;

    public ItemsService(IItemsRepository itemsRepository)
    {
        _itemsRepository = itemsRepository;
    }

    public async Task<IList<Item>?> GetItemsAsync(long todoListId)
    {
        if (!await _itemsRepository.TodoListExistsAsync(todoListId))
        {
            return null;
        }

        return await _itemsRepository.GetItemsAsync(todoListId);
    }

    public Task<Item?> GetItemAsync(long todoListId, long id)
    {
        return _itemsRepository.GetItemAsync(todoListId, id);
    }

    public async Task<Item?> CreateItemAsync(long todoListId, CreateItem payload)
    {
        if (!await _itemsRepository.TodoListExistsAsync(todoListId))
        {
            return null;
        }

        var createdAt = payload.CreatedAt ?? DateTimeOffset.UtcNow;
        var item = new Item
        {
            SourceId = payload.SourceId,
            Name = payload.Name,
            IsCompleted = payload.IsCompleted,
            CreatedAt = createdAt,
            UpdatedAt = payload.UpdatedAt ?? createdAt,
            TodoListId = todoListId,
        };

        return await _itemsRepository.AddItemAsync(item);
    }

    public async Task<Item?> UpdateItemAsync(long todoListId, long id, UpdateItem payload)
    {
        var item = await _itemsRepository.GetItemAsync(todoListId, id);

        if (item == null)
        {
            return null;
        }

        if (payload.SourceId != null)
        {
            item.SourceId = payload.SourceId;
        }

        item.Name = payload.Name;
        item.IsCompleted = payload.IsCompleted;
        item.UpdatedAt = payload.UpdatedAt ?? DateTimeOffset.UtcNow;

        return await _itemsRepository.UpdateItemAsync(item);
    }

    public async Task<bool> DeleteItemAsync(long todoListId, long id)
    {
        var item = await _itemsRepository.GetItemAsync(todoListId, id);

        if (item == null)
        {
            return false;
        }

        await _itemsRepository.SoftDeleteItemAsync(item, DateTimeOffset.UtcNow);
        return true;
    }
}

using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;
using TodoApi.Sync;

namespace TodoApi.Services;

public class ItemsService : IItemsService
{
    private readonly IItemsRepository _itemsRepository;
    private readonly ISyncEventPublisher _syncEventPublisher;

    public ItemsService(
        IItemsRepository itemsRepository,
        ISyncEventPublisher? syncEventPublisher = null
    )
    {
        _itemsRepository = itemsRepository;
        _syncEventPublisher = syncEventPublisher ?? new NoOpSyncEventPublisher();
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

        var createdItem = await _itemsRepository.AddItemAsync(item);

        await _syncEventPublisher.PublishAsync(
            SyncEntityTypes.Item,
            createdItem.Id,
            SyncEventTypes.Created,
            createdItem
        );

        return createdItem;
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

        var updatedItem = await _itemsRepository.UpdateItemAsync(item);

        await _syncEventPublisher.PublishAsync(
            SyncEntityTypes.Item,
            updatedItem.Id,
            SyncEventTypes.Updated,
            updatedItem
        );

        return updatedItem;
    }

    public async Task<bool> DeleteItemAsync(long todoListId, long id)
    {
        var item = await _itemsRepository.GetItemAsync(todoListId, id);

        if (item == null)
        {
            return false;
        }

        await _itemsRepository.SoftDeleteItemAsync(item, DateTimeOffset.UtcNow);
        await _syncEventPublisher.PublishAsync(
            SyncEntityTypes.Item,
            item.Id,
            SyncEventTypes.Deleted,
            item
        );

        return true;
    }
}

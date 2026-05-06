using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Realtime;
using TodoApi.Repositories;
using TodoApi.Sync;

namespace TodoApi.Services;

public class ItemsService : IItemsService
{
    private readonly IItemsRepository _itemsRepository;
    private readonly ISyncEventPublisher _syncEventPublisher;
    private readonly ITodoRealtimeNotifier _todoRealtimeNotifier;

    public ItemsService(
        IItemsRepository itemsRepository,
        ISyncEventPublisher? syncEventPublisher = null,
        ITodoRealtimeNotifier? todoRealtimeNotifier = null
    )
    {
        _itemsRepository = itemsRepository;
        _syncEventPublisher = syncEventPublisher ?? new NoOpSyncEventPublisher();
        _todoRealtimeNotifier = todoRealtimeNotifier ?? new NoOpTodoRealtimeNotifier();
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
        await _todoRealtimeNotifier.PublishAsync(
            new TodoRealtimeEvent
            {
                EventType = TodoRealtimeEventTypes.ItemCreated,
                EntityType = SyncEntityTypes.Item,
                EntityId = createdItem.Id,
                TodoListId = createdItem.TodoListId,
                Source = TodoRealtimeSources.LocalApi,
                Payload = createdItem,
            }
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
        await _todoRealtimeNotifier.PublishAsync(
            new TodoRealtimeEvent
            {
                EventType = TodoRealtimeEventTypes.ItemUpdated,
                EntityType = SyncEntityTypes.Item,
                EntityId = updatedItem.Id,
                TodoListId = updatedItem.TodoListId,
                Source = TodoRealtimeSources.LocalApi,
                Payload = updatedItem,
            }
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
        await _todoRealtimeNotifier.PublishAsync(
            new TodoRealtimeEvent
            {
                EventType = TodoRealtimeEventTypes.ItemDeleted,
                EntityType = SyncEntityTypes.Item,
                EntityId = item.Id,
                TodoListId = item.TodoListId,
                Source = TodoRealtimeSources.LocalApi,
                Payload = item,
            }
        );

        return true;
    }
}

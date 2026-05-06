using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Realtime;
using TodoApi.Repositories;
using TodoApi.Sync;

namespace TodoApi.Services;

public class TodoListsService : ITodoListsService
{
    private readonly ISyncEventPublisher _syncEventPublisher;
    private readonly ITodoRealtimeNotifier _todoRealtimeNotifier;
    private readonly ITodoListsRepository _todoListsRepository;

    public TodoListsService(
        ITodoListsRepository todoListsRepository,
        ISyncEventPublisher? syncEventPublisher = null,
        ITodoRealtimeNotifier? todoRealtimeNotifier = null
    )
    {
        _todoListsRepository = todoListsRepository;
        _syncEventPublisher = syncEventPublisher ?? new NoOpSyncEventPublisher();
        _todoRealtimeNotifier = todoRealtimeNotifier ?? new NoOpTodoRealtimeNotifier();
    }

    public Task<IList<TodoList>> GetTodoListsAsync()
    {
        return _todoListsRepository.GetTodoListsAsync();
    }

    public Task<TodoList?> GetTodoListAsync(long id)
    {
        return _todoListsRepository.GetTodoListAsync(id);
    }

    public async Task<TodoListDetail?> GetTodoListDetailAsync(long id)
    {
        var todoList = await _todoListsRepository.GetTodoListWithItemsAsync(id);

        if (todoList == null)
        {
            return null;
        }

        return new TodoListDetail
        {
            Id = todoList.Id,
            SourceId = todoList.SourceId,
            ExternalId = todoList.ExternalId,
            Name = todoList.Name,
            CreatedAt = todoList.CreatedAt,
            UpdatedAt = todoList.UpdatedAt,
            IsDeleted = todoList.IsDeleted,
            DeletedAt = todoList.DeletedAt,
            Items = todoList.Items.ToList(),
        };
    }

    public async Task<TodoList> CreateTodoListAsync(CreateTodoList payload)
    {
        var createdAt = payload.CreatedAt ?? DateTimeOffset.UtcNow;
        var todoList = new TodoList
        {
            SourceId = payload.SourceId,
            Name = payload.Name,
            CreatedAt = createdAt,
            UpdatedAt = payload.UpdatedAt ?? createdAt,
        };

        var createdTodoList = await _todoListsRepository.AddTodoListAsync(todoList);

        await _syncEventPublisher.PublishAsync(
            SyncEntityTypes.TodoList,
            createdTodoList.Id,
            SyncEventTypes.Created,
            createdTodoList
        );
        await _todoRealtimeNotifier.PublishAsync(
            new TodoRealtimeEvent
            {
                EventType = TodoRealtimeEventTypes.TodoListCreated,
                EntityType = SyncEntityTypes.TodoList,
                EntityId = createdTodoList.Id,
                TodoListId = createdTodoList.Id,
                Source = TodoRealtimeSources.LocalApi,
                Payload = createdTodoList,
            }
        );

        return createdTodoList;
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

        var updatedTodoList = await _todoListsRepository.UpdateTodoListAsync(todoList);

        await _syncEventPublisher.PublishAsync(
            SyncEntityTypes.TodoList,
            updatedTodoList.Id,
            SyncEventTypes.Updated,
            updatedTodoList
        );
        await _todoRealtimeNotifier.PublishAsync(
            new TodoRealtimeEvent
            {
                EventType = TodoRealtimeEventTypes.TodoListUpdated,
                EntityType = SyncEntityTypes.TodoList,
                EntityId = updatedTodoList.Id,
                TodoListId = updatedTodoList.Id,
                Source = TodoRealtimeSources.LocalApi,
                Payload = updatedTodoList,
            }
        );

        return updatedTodoList;
    }

    public async Task<bool> DeleteTodoListAsync(long id)
    {
        var todoList = await _todoListsRepository.GetTodoListAsync(id);

        if (todoList == null)
        {
            return false;
        }

        await _todoListsRepository.SoftDeleteTodoListAsync(todoList, DateTimeOffset.UtcNow);
        await _syncEventPublisher.PublishAsync(
            SyncEntityTypes.TodoList,
            todoList.Id,
            SyncEventTypes.Deleted,
            todoList
        );
        await _todoRealtimeNotifier.PublishAsync(
            new TodoRealtimeEvent
            {
                EventType = TodoRealtimeEventTypes.TodoListDeleted,
                EntityType = SyncEntityTypes.TodoList,
                EntityId = todoList.Id,
                TodoListId = todoList.Id,
                Source = TodoRealtimeSources.LocalApi,
                Payload = todoList,
            }
        );

        return true;
    }
}

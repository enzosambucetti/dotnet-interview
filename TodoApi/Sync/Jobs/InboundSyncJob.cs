using Microsoft.EntityFrameworkCore;
using TodoApi.External;
using TodoApi.Models;
using TodoApi.Realtime;

namespace TodoApi.Sync.Jobs;

public class InboundSyncJob : IInboundSyncJob
{
    private readonly IExternalTodoApiClient _externalTodoApiClient;
    private readonly ITodoRealtimeNotifier _todoRealtimeNotifier;
    private readonly ILogger<InboundSyncJob> _logger;
    private readonly TodoContext _context;

    public InboundSyncJob(
        TodoContext context,
        IExternalTodoApiClient externalTodoApiClient,
        ILogger<InboundSyncJob> logger,
        ITodoRealtimeNotifier? todoRealtimeNotifier = null
    )
    {
        _context = context;
        _externalTodoApiClient = externalTodoApiClient;
        _logger = logger;
        _todoRealtimeNotifier = todoRealtimeNotifier ?? new NoOpTodoRealtimeNotifier();
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var realtimeEvents = new List<TodoRealtimeEvent>();
        var externalLists = await _externalTodoApiClient.ListTodoListsAsync(cancellationToken);
        var seenExternalListIds = externalLists
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => x.Id!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var externalList in externalLists)
        {
            if (string.IsNullOrWhiteSpace(externalList.Id))
            {
                continue;
            }

            await UpsertTodoListAsync(externalList, realtimeEvents, cancellationToken);
        }

        await SoftDeleteMissingExternalListsAsync(seenExternalListIds, realtimeEvents, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        realtimeEvents.Add(
            new TodoRealtimeEvent
            {
                EventType = TodoRealtimeEventTypes.InboundSyncCompleted,
                EntityType = "Sync",
                Source = TodoRealtimeSources.InboundSync,
                Payload = new { changed = realtimeEvents.Count },
            }
        );
        await _todoRealtimeNotifier.PublishManyAsync(realtimeEvents, cancellationToken);
    }

    private async Task UpsertTodoListAsync(
        ExternalTodoList externalList,
        IList<TodoRealtimeEvent> realtimeEvents,
        CancellationToken cancellationToken
    )
    {
        var todoList = await FindTodoListAsync(externalList, cancellationToken);

        if (todoList == null)
        {
            todoList = new TodoList
            {
                ExternalId = externalList.Id,
                SourceId = externalList.SourceId,
                Name = externalList.Name ?? string.Empty,
                CreatedAt = externalList.CreatedAt,
                UpdatedAt = externalList.UpdatedAt,
            };

            _context.TodoList.Add(todoList);
            realtimeEvents.Add(CreateTodoListEvent(TodoRealtimeEventTypes.TodoListCreated, todoList));
        }
        else
        {
            todoList.ExternalId ??= externalList.Id;
            todoList.SourceId ??= externalList.SourceId;

            if (!todoList.IsDeleted)
            {
                if (ApplyExternalListUpdate(todoList, externalList))
                {
                    realtimeEvents.Add(CreateTodoListEvent(TodoRealtimeEventTypes.TodoListUpdated, todoList));
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await UpsertItemsAsync(todoList, externalList, realtimeEvents, cancellationToken);
    }

    private bool ApplyExternalListUpdate(TodoList todoList, ExternalTodoList externalList)
    {
        var changed = todoList.Name != (externalList.Name ?? string.Empty);

        if (!changed)
        {
            return false;
        }

        if (todoList.UpdatedAt > externalList.UpdatedAt)
        {
            _logger.LogWarning(
                "Inbound sync conflict for TodoList. LocalId: {LocalId}; ExternalId: {ExternalId}",
                todoList.Id,
                todoList.ExternalId
            );
            return false;
        }

        todoList.SourceId = externalList.SourceId;
        todoList.Name = externalList.Name ?? string.Empty;
        todoList.UpdatedAt = externalList.UpdatedAt;
        return true;
    }

    private async Task UpsertItemsAsync(
        TodoList todoList,
        ExternalTodoList externalList,
        IList<TodoRealtimeEvent> realtimeEvents,
        CancellationToken cancellationToken
    )
    {
        var seenExternalItemIds = externalList.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => x.Id!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var externalItem in externalList.Items)
        {
            if (string.IsNullOrWhiteSpace(externalItem.Id))
            {
                continue;
            }

            var item = await FindItemAsync(externalItem, cancellationToken);

            if (item == null)
            {
                var newItem = new Item
                {
                    ExternalId = externalItem.Id,
                    SourceId = externalItem.SourceId,
                    Name = externalItem.Description ?? string.Empty,
                    IsCompleted = externalItem.Completed,
                    CreatedAt = externalItem.CreatedAt,
                    UpdatedAt = externalItem.UpdatedAt,
                    TodoListId = todoList.Id,
                };
                _context.Items.Add(newItem);
                realtimeEvents.Add(CreateItemEvent(TodoRealtimeEventTypes.ItemCreated, newItem));
            }
            else
            {
                item.ExternalId ??= externalItem.Id;
                item.SourceId ??= externalItem.SourceId;

                if (!item.IsDeleted)
                {
                    if (ApplyExternalItemUpdate(item, externalItem))
                    {
                        realtimeEvents.Add(CreateItemEvent(TodoRealtimeEventTypes.ItemUpdated, item));
                    }
                }
            }
        }

        await SoftDeleteMissingExternalItemsAsync(
            todoList,
            seenExternalItemIds,
            realtimeEvents,
            cancellationToken
        );
    }

    private bool ApplyExternalItemUpdate(Item item, ExternalTodoItem externalItem)
    {
        var changed =
            item.Name != (externalItem.Description ?? string.Empty)
            || item.IsCompleted != externalItem.Completed;

        if (!changed)
        {
            return false;
        }

        if (item.UpdatedAt > externalItem.UpdatedAt)
        {
            _logger.LogWarning(
                "Inbound sync conflict for Item. LocalId: {LocalId}; ExternalId: {ExternalId}",
                item.Id,
                item.ExternalId
            );
            return false;
        }

        item.SourceId = externalItem.SourceId;
        item.Name = externalItem.Description ?? string.Empty;
        item.IsCompleted = externalItem.Completed;
        item.UpdatedAt = externalItem.UpdatedAt;
        return true;
    }

    private async Task SoftDeleteMissingExternalListsAsync(
        HashSet<string> seenExternalListIds,
        IList<TodoRealtimeEvent> realtimeEvents,
        CancellationToken cancellationToken
    )
    {
        var deletedAt = DateTimeOffset.UtcNow;
        var missingLists = await _context.TodoList
            .IgnoreQueryFilters()
            .Where(x => x.ExternalId != null && !x.IsDeleted && !seenExternalListIds.Contains(x.ExternalId))
            .ToListAsync(cancellationToken);

        foreach (var todoList in missingLists)
        {
            todoList.IsDeleted = true;
            todoList.DeletedAt = deletedAt;
            todoList.UpdatedAt = deletedAt;
            realtimeEvents.Add(CreateTodoListEvent(TodoRealtimeEventTypes.TodoListDeleted, todoList));

            var items = await _context.Items
                .Where(x => x.TodoListId == todoList.Id)
                .ToListAsync(cancellationToken);

            foreach (var item in items)
            {
                item.IsDeleted = true;
                item.DeletedAt = deletedAt;
                item.UpdatedAt = deletedAt;
                realtimeEvents.Add(CreateItemEvent(TodoRealtimeEventTypes.ItemDeleted, item));
            }
        }
    }

    private async Task SoftDeleteMissingExternalItemsAsync(
        TodoList todoList,
        HashSet<string> seenExternalItemIds,
        IList<TodoRealtimeEvent> realtimeEvents,
        CancellationToken cancellationToken
    )
    {
        var deletedAt = DateTimeOffset.UtcNow;
        var missingItems = await _context.Items
            .IgnoreQueryFilters()
            .Where(
                x =>
                    x.TodoListId == todoList.Id
                    && x.ExternalId != null
                    && !x.IsDeleted
                    && !seenExternalItemIds.Contains(x.ExternalId)
            )
            .ToListAsync(cancellationToken);

        foreach (var item in missingItems)
        {
            item.IsDeleted = true;
            item.DeletedAt = deletedAt;
            item.UpdatedAt = deletedAt;
            realtimeEvents.Add(CreateItemEvent(TodoRealtimeEventTypes.ItemDeleted, item));
        }
    }

    private static TodoRealtimeEvent CreateTodoListEvent(string eventType, TodoList todoList)
    {
        return new TodoRealtimeEvent
        {
            EventType = eventType,
            EntityType = SyncEntityTypes.TodoList,
            EntityId = todoList.Id,
            TodoListId = todoList.Id,
            Source = TodoRealtimeSources.InboundSync,
            Payload = todoList,
        };
    }

    private static TodoRealtimeEvent CreateItemEvent(string eventType, Item item)
    {
        return new TodoRealtimeEvent
        {
            EventType = eventType,
            EntityType = SyncEntityTypes.Item,
            EntityId = item.Id,
            TodoListId = item.TodoListId,
            Source = TodoRealtimeSources.InboundSync,
            Payload = item,
        };
    }

    private async Task<TodoList?> FindTodoListAsync(
        ExternalTodoList externalList,
        CancellationToken cancellationToken
    )
    {
        var todoList = await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ExternalId == externalList.Id, cancellationToken);

        if (todoList != null || string.IsNullOrWhiteSpace(externalList.SourceId))
        {
            return todoList;
        }

        todoList = await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.SourceId == externalList.SourceId, cancellationToken);

        if (todoList != null)
        {
            return todoList;
        }

        if (!long.TryParse(externalList.SourceId, out var localId))
        {
            return null;
        }

        return await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == localId, cancellationToken);
    }

    private async Task<Item?> FindItemAsync(
        ExternalTodoItem externalItem,
        CancellationToken cancellationToken
    )
    {
        var item = await _context.Items
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ExternalId == externalItem.Id, cancellationToken);

        if (item != null || string.IsNullOrWhiteSpace(externalItem.SourceId))
        {
            return item;
        }

        item = await _context.Items
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.SourceId == externalItem.SourceId, cancellationToken);

        if (item != null)
        {
            return item;
        }

        if (!long.TryParse(externalItem.SourceId, out var localId))
        {
            return null;
        }

        return await _context.Items
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == localId, cancellationToken);
    }
}

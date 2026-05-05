using System.Net;
using Microsoft.EntityFrameworkCore;
using TodoApi.External;
using TodoApi.Models;

namespace TodoApi.Sync.Jobs;

public class OutboundSyncJob : IOutboundSyncJob
{
    private readonly IExternalTodoApiClient _externalTodoApiClient;
    private readonly IInboundSyncJob _inboundSyncJob;
    private readonly ILogger<OutboundSyncJob> _logger;
    private readonly TodoContext _context;

    public OutboundSyncJob(
        TodoContext context,
        IExternalTodoApiClient externalTodoApiClient,
        IInboundSyncJob inboundSyncJob,
        ILogger<OutboundSyncJob> logger
    )
    {
        _context = context;
        _externalTodoApiClient = externalTodoApiClient;
        _inboundSyncJob = inboundSyncJob;
        _logger = logger;
    }

    public async Task ProcessAsync(long syncEventId, CancellationToken cancellationToken)
    {
        var syncEvent = await _context.SyncEvents.FirstOrDefaultAsync(
            x => x.Id == syncEventId,
            cancellationToken
        );

        if (syncEvent == null || IsFinalStatus(syncEvent.Status))
        {
            return;
        }

        if (await TryCompleteObsoleteEventAsync(syncEvent, cancellationToken))
        {
            return;
        }

        syncEvent.Status = SyncEventStatuses.Processing;
        syncEvent.Attempts++;
        syncEvent.LastError = null;
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await ProcessEventAsync(syncEvent, cancellationToken);

            syncEvent.Status = SyncEventStatuses.Completed;
            syncEvent.ProcessedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (ExternalApiException exception) when (
            exception.StatusCode == HttpStatusCode.NotFound
            && syncEvent.EventType == SyncEventTypes.Deleted
        )
        {
            _logger.LogInformation(
                "External delete target was already missing. SyncEventId: {SyncEventId}",
                syncEvent.Id
            );

            syncEvent.Status = SyncEventStatuses.Completed;
            syncEvent.ProcessedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (ExternalApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await HandleExternalNotFoundAsync(syncEvent, exception, cancellationToken);
        }
        catch (SyncDependencyPendingException exception)
        {
            await MarkPendingAsync(syncEvent, exception.Message, cancellationToken);
        }
        catch (SyncTerminalException exception)
        {
            await MarkFailedAsync(syncEvent, exception, retryable: false, cancellationToken);
        }
        catch (Exception exception)
        {
            if (await TryCompleteCreateAfterReconciliationAsync(syncEvent, exception, cancellationToken))
            {
                return;
            }

            await MarkFailedAsync(
                syncEvent,
                exception,
                IsRetryableFailure(exception),
                cancellationToken
            );
        }
    }

    private async Task HandleExternalNotFoundAsync(
        SyncEvent syncEvent,
        ExternalApiException exception,
        CancellationToken cancellationToken
    )
    {
        Exception failure = exception;

        try
        {
            _logger.LogWarning(
                exception,
                "Outbound sync received 404. Running inbound reconciliation. SyncEventId: {SyncEventId}; EntityType: {EntityType}; EntityId: {EntityId}; EventType: {EventType}",
                syncEvent.Id,
                syncEvent.EntityType,
                syncEvent.EntityId,
                syncEvent.EventType
            );

            await _inboundSyncJob.ProcessAsync(cancellationToken);

            if (await IsEntityDeletedAfterReconciliationAsync(syncEvent, cancellationToken))
            {
                _logger.LogInformation(
                    "Outbound sync 404 was reconciled as externally deleted. Completing SyncEventId: {SyncEventId}",
                    syncEvent.Id
                );

                syncEvent.Status = SyncEventStatuses.Completed;
                syncEvent.LastError = null;
                syncEvent.ProcessedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            _logger.LogWarning(
                "Outbound sync 404 was not resolved by inbound reconciliation. SyncEventId: {SyncEventId}",
                syncEvent.Id
            );
        }
        catch (Exception reconciliationException)
        {
            _logger.LogError(
                reconciliationException,
                "Inbound reconciliation failed after outbound 404. SyncEventId: {SyncEventId}",
                syncEvent.Id
            );
            failure = reconciliationException;
        }

        await MarkFailedAsync(syncEvent, failure, retryable: true, cancellationToken);
    }

    private async Task<bool> IsEntityDeletedAfterReconciliationAsync(
        SyncEvent syncEvent,
        CancellationToken cancellationToken
    )
    {
        if (syncEvent.EntityType == SyncEntityTypes.TodoList)
        {
            var todoList = await _context.TodoList
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

            return todoList == null || todoList.IsDeleted;
        }

        if (syncEvent.EntityType == SyncEntityTypes.Item)
        {
            var item = await _context.Items
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

            if (item == null || item.IsDeleted)
            {
                return true;
            }

            var todoList = await _context.TodoList
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == item.TodoListId, cancellationToken);

            return todoList == null || todoList.IsDeleted;
        }

        return false;
    }

    private async Task<bool> TryCompleteCreateAfterReconciliationAsync(
        SyncEvent syncEvent,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (syncEvent.EventType != SyncEventTypes.Created || !ShouldReconcileCreateFailure(exception))
        {
            return false;
        }

        try
        {
            _logger.LogWarning(
                exception,
                "Outbound create failed with an ambiguous or conflicting result. Running inbound reconciliation. SyncEventId: {SyncEventId}; EntityType: {EntityType}; EntityId: {EntityId}",
                syncEvent.Id,
                syncEvent.EntityType,
                syncEvent.EntityId
            );

            await _inboundSyncJob.ProcessAsync(cancellationToken);

            if (await HasExternalIdAfterReconciliationAsync(syncEvent, cancellationToken))
            {
                syncEvent.Status = SyncEventStatuses.Completed;
                syncEvent.LastError = null;
                syncEvent.ProcessedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
        }
        catch (Exception reconciliationException)
        {
            _logger.LogError(
                reconciliationException,
                "Inbound reconciliation failed after outbound create failure. SyncEventId: {SyncEventId}",
                syncEvent.Id
            );
        }

        return false;
    }

    private async Task<bool> HasExternalIdAfterReconciliationAsync(
        SyncEvent syncEvent,
        CancellationToken cancellationToken
    )
    {
        if (syncEvent.EntityType == SyncEntityTypes.TodoList)
        {
            var todoList = await _context.TodoList
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

            return !string.IsNullOrWhiteSpace(todoList?.ExternalId) || todoList?.IsDeleted == true;
        }

        if (syncEvent.EntityType == SyncEntityTypes.Item)
        {
            var item = await _context.Items
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

            return !string.IsNullOrWhiteSpace(item?.ExternalId)
                || item?.IsDeleted == true
                || await IsEntityDeletedAfterReconciliationAsync(syncEvent, cancellationToken);
        }

        return false;
    }

    private async Task<bool> TryCompleteObsoleteEventAsync(
        SyncEvent syncEvent,
        CancellationToken cancellationToken
    )
    {
        if (syncEvent.EventType != SyncEventTypes.Created && syncEvent.EventType != SyncEventTypes.Updated)
        {
            return false;
        }

        var isObsolete = false;

        if (syncEvent.EntityType == SyncEntityTypes.TodoList)
        {
            var todoList = await _context.TodoList
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

            isObsolete = todoList?.IsDeleted == true;
        }
        else if (syncEvent.EntityType == SyncEntityTypes.Item)
        {
            var item = await _context.Items
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

            if (item?.IsDeleted == true)
            {
                isObsolete = true;
            }
            else if (item != null)
            {
                var todoList = await _context.TodoList
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == item.TodoListId, cancellationToken);

                isObsolete = todoList == null || todoList.IsDeleted;
            }
        }

        if (!isObsolete)
        {
            return false;
        }

        _logger.LogInformation(
            "Completing obsolete outbound event for soft-deleted entity. SyncEventId: {SyncEventId}; EntityType: {EntityType}; EntityId: {EntityId}; EventType: {EventType}",
            syncEvent.Id,
            syncEvent.EntityType,
            syncEvent.EntityId,
            syncEvent.EventType
        );

        syncEvent.Status = SyncEventStatuses.Completed;
        syncEvent.LastError = null;
        syncEvent.ProcessedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task MarkFailedAsync(
        SyncEvent syncEvent,
        Exception exception,
        bool retryable,
        CancellationToken cancellationToken
    )
    {
        _logger.LogError(
            exception,
            "Outbound sync failed. SyncEventId: {SyncEventId}; CorrelationId: {CorrelationId}",
            syncEvent.Id,
            syncEvent.CorrelationId
        );

        syncEvent.Status = retryable
            ? SyncEventStatuses.FailedRetryable
            : SyncEventStatuses.FailedTerminal;
        syncEvent.LastError = exception.Message;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkPendingAsync(
        SyncEvent syncEvent,
        string reason,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation(
            "Outbound sync dependency is pending. SyncEventId: {SyncEventId}; Reason: {Reason}",
            syncEvent.Id,
            reason
        );

        syncEvent.Status = SyncEventStatuses.Pending;
        syncEvent.LastError = reason;
        syncEvent.Attempts = Math.Max(0, syncEvent.Attempts - 1);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> HasPendingCreateAsync(
        SyncEvent syncEvent,
        CancellationToken cancellationToken
    )
    {
        return _context.SyncEvents.AnyAsync(
            x =>
                x.Id != syncEvent.Id
                && x.EntityType == syncEvent.EntityType
                && x.EntityId == syncEvent.EntityId
                && x.EventType == SyncEventTypes.Created
                && (
                    x.Status == SyncEventStatuses.Pending
                    || x.Status == SyncEventStatuses.Processing
                    || x.Status == SyncEventStatuses.FailedRetryable
                    || x.Status == SyncEventStatuses.Failed
                ),
            cancellationToken
        );
    }

    private static bool IsFinalStatus(string status)
    {
        return status == SyncEventStatuses.Completed || status == SyncEventStatuses.FailedTerminal;
    }

    private static bool ShouldReconcileCreateFailure(Exception exception)
    {
        return exception is TaskCanceledException
            || exception is TimeoutException
            || exception is HttpRequestException
            || exception is ExternalApiException { StatusCode: HttpStatusCode.Conflict }
            || (
                exception is ExternalApiException externalApiException
                && (int)externalApiException.StatusCode >= 500
            );
    }

    private static bool IsRetryableFailure(Exception exception)
    {
        if (exception is ExternalApiException externalApiException)
        {
            return externalApiException.StatusCode == HttpStatusCode.RequestTimeout
                || externalApiException.StatusCode == HttpStatusCode.TooManyRequests
                || (int)externalApiException.StatusCode >= 500;
        }

        return exception is TaskCanceledException
            or TimeoutException
            or HttpRequestException
            or SyncDependencyPendingException;
    }

    private sealed class SyncDependencyPendingException : Exception
    {
        public SyncDependencyPendingException(string message)
            : base(message) { }
    }

    private sealed class SyncTerminalException : Exception
    {
        public SyncTerminalException(string message)
            : base(message) { }
    }

    private Task ProcessEventAsync(SyncEvent syncEvent, CancellationToken cancellationToken)
    {
        return (syncEvent.EntityType, syncEvent.EventType) switch
        {
            (SyncEntityTypes.TodoList, SyncEventTypes.Created) =>
                ProcessTodoListCreatedAsync(syncEvent, cancellationToken),
            (SyncEntityTypes.TodoList, SyncEventTypes.Updated) =>
                ProcessTodoListUpdatedAsync(syncEvent, cancellationToken),
            (SyncEntityTypes.TodoList, SyncEventTypes.Deleted) =>
                ProcessTodoListDeletedAsync(syncEvent.EntityId, cancellationToken),
            (SyncEntityTypes.Item, SyncEventTypes.Created) =>
                ProcessItemCreatedAsync(syncEvent, cancellationToken),
            (SyncEntityTypes.Item, SyncEventTypes.Updated) =>
                ProcessItemUpdatedAsync(syncEvent, cancellationToken),
            (SyncEntityTypes.Item, SyncEventTypes.Deleted) =>
                ProcessItemDeletedAsync(syncEvent.EntityId, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported sync event {syncEvent.EntityType}/{syncEvent.EventType}."
            ),
        };
    }

    private async Task ProcessTodoListCreatedAsync(SyncEvent syncEvent, CancellationToken cancellationToken)
    {
        var todoList = await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

        if (todoList == null)
        {
            throw new SyncTerminalException($"TodoList '{syncEvent.EntityId}' was not found.");
        }

        if (todoList.IsDeleted || !string.IsNullOrWhiteSpace(todoList.ExternalId))
        {
            return;
        }

        var items = await _context.Items
            .IgnoreQueryFilters()
            .Where(x => x.TodoListId == todoList.Id && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        var externalTodoList = await _externalTodoApiClient.CreateTodoListAsync(
            new CreateExternalTodoListRequest
            {
                SourceId = todoList.Id.ToString(),
                Name = todoList.Name,
                Items = items
                    .Select(item => new CreateExternalTodoItemRequest
                    {
                        SourceId = item.Id.ToString(),
                        Description = item.Name,
                        Completed = item.IsCompleted,
                    })
                    .ToList(),
            },
            cancellationToken
        );

        todoList.ExternalId = externalTodoList.Id;
        todoList.SourceId ??= externalTodoList.SourceId;

        foreach (var externalItem in externalTodoList.Items)
        {
            if (!long.TryParse(externalItem.SourceId, out var localItemId))
            {
                continue;
            }

            var localItem = items.FirstOrDefault(x => x.Id == localItemId);

            if (localItem != null)
            {
                localItem.ExternalId = externalItem.Id;
                localItem.SourceId ??= externalItem.SourceId;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessTodoListUpdatedAsync(SyncEvent syncEvent, CancellationToken cancellationToken)
    {
        var todoList = await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

        if (todoList == null)
        {
            throw new SyncTerminalException($"TodoList '{syncEvent.EntityId}' was not found.");
        }

        if (todoList.IsDeleted)
        {
            return;
        }

        if (todoList.ExternalId == null)
        {
            if (await HasPendingCreateAsync(syncEvent, cancellationToken))
            {
                throw new SyncDependencyPendingException(
                    $"TodoList '{syncEvent.EntityId}' is waiting for its create sync to set ExternalId."
                );
            }

            throw new SyncTerminalException($"TodoList '{syncEvent.EntityId}' does not have ExternalId.");
        }

        await _externalTodoApiClient.UpdateTodoListAsync(
            todoList.ExternalId,
            new UpdateExternalTodoListRequest { Name = todoList.Name },
            cancellationToken
        );
    }

    private async Task ProcessTodoListDeletedAsync(long todoListId, CancellationToken cancellationToken)
    {
        var todoList = await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == todoListId, cancellationToken);

        if (todoList?.ExternalId == null)
        {
            return;
        }

        await _externalTodoApiClient.DeleteTodoListAsync(todoList.ExternalId, cancellationToken);
    }

    private async Task ProcessItemCreatedAsync(SyncEvent syncEvent, CancellationToken cancellationToken)
    {
        var item = await _context.Items
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

        if (item == null)
        {
            throw new SyncTerminalException($"Item '{syncEvent.EntityId}' was not found.");
        }

        if (item.IsDeleted || !string.IsNullOrWhiteSpace(item.ExternalId))
        {
            return;
        }

        var todoList = await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == item.TodoListId, cancellationToken);

        if (todoList == null || todoList.IsDeleted)
        {
            return;
        }

        if (todoList.ExternalId == null)
        {
            throw new SyncDependencyPendingException(
                $"TodoList '{item.TodoListId}' does not have ExternalId for item create."
            );
        }

        var externalItem = await _externalTodoApiClient.CreateTodoItemAsync(
            todoList.ExternalId,
            new CreateExternalTodoItemRequest
            {
                SourceId = item.Id.ToString(),
                Description = item.Name,
                Completed = item.IsCompleted,
            },
            cancellationToken
        );

        item.ExternalId = externalItem.Id;
        item.SourceId ??= externalItem.SourceId;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessItemUpdatedAsync(SyncEvent syncEvent, CancellationToken cancellationToken)
    {
        var item = await _context.Items
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == syncEvent.EntityId, cancellationToken);

        if (item == null)
        {
            throw new SyncTerminalException($"Item '{syncEvent.EntityId}' was not found.");
        }

        if (item.IsDeleted)
        {
            return;
        }

        if (item.ExternalId == null)
        {
            if (await HasPendingCreateAsync(syncEvent, cancellationToken))
            {
                throw new SyncDependencyPendingException(
                    $"Item '{syncEvent.EntityId}' is waiting for its create sync to set ExternalId."
                );
            }

            throw new SyncTerminalException($"Item '{syncEvent.EntityId}' does not have ExternalId.");
        }

        var todoList = await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == item.TodoListId, cancellationToken);

        if (todoList == null || todoList.IsDeleted)
        {
            return;
        }

        if (todoList.ExternalId == null)
        {
            throw new SyncDependencyPendingException(
                $"TodoList '{item.TodoListId}' does not have ExternalId for item update."
            );
        }

        await _externalTodoApiClient.UpdateTodoItemAsync(
            todoList.ExternalId,
            item.ExternalId,
            new UpdateExternalTodoItemRequest
            {
                Description = item.Name,
                Completed = item.IsCompleted,
            },
            cancellationToken
        );
    }

    private async Task ProcessItemDeletedAsync(long itemId, CancellationToken cancellationToken)
    {
        var item = await _context.Items
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken);

        if (item?.ExternalId == null)
        {
            return;
        }

        var todoList = await _context.TodoList
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == item.TodoListId, cancellationToken);

        if (todoList?.ExternalId == null)
        {
            return;
        }

        await _externalTodoApiClient.DeleteTodoItemAsync(
            todoList.ExternalId,
            item.ExternalId,
            cancellationToken
        );
    }
}

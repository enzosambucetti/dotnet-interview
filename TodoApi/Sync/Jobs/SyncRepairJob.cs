using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Sync.Jobs;

public class SyncRepairJob : ISyncRepairJob
{
    private const int MaxAttempts = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoContext _context;
    private readonly IInboundSyncJob _inboundSyncJob;
    private readonly ILogger<SyncRepairJob> _logger;
    private readonly ISyncJobScheduler _syncJobScheduler;

    public SyncRepairJob(
        TodoContext context,
        IInboundSyncJob inboundSyncJob,
        ISyncJobScheduler syncJobScheduler,
        ILogger<SyncRepairJob> logger
    )
    {
        _context = context;
        _inboundSyncJob = inboundSyncJob;
        _syncJobScheduler = syncJobScheduler;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Manual sync repair started.");

        await ReconcileInboundBestEffortAsync(cancellationToken);

        var syncEventIdsToEnqueue = new List<long>();
        syncEventIdsToEnqueue.AddRange(await ReopenExhaustedRetryableEventsAsync(cancellationToken));

        var createdSyncEvents = new List<SyncEvent>();
        createdSyncEvents.AddRange(await CreateMissingTodoListCreateEventsAsync(cancellationToken));
        createdSyncEvents.AddRange(await CreateMissingItemCreateEventsAsync(cancellationToken));

        if (createdSyncEvents.Count > 0)
        {
            _context.SyncEvents.AddRange(createdSyncEvents);
            await _context.SaveChangesAsync(cancellationToken);
            syncEventIdsToEnqueue.AddRange(createdSyncEvents.Select(syncEvent => syncEvent.Id));
        }

        foreach (var syncEventId in syncEventIdsToEnqueue.Distinct())
        {
            _syncJobScheduler.EnqueueOutboundSync(syncEventId);
        }

        _logger.LogInformation(
            "Manual sync repair completed. EnqueuedSyncEvents: {EnqueuedSyncEvents}; CreatedSyncEvents: {CreatedSyncEvents}",
            syncEventIdsToEnqueue.Count,
            createdSyncEvents.Count
        );
    }

    private async Task ReconcileInboundBestEffortAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _inboundSyncJob.ProcessAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Manual sync repair could not run inbound reconciliation before repair."
            );
        }
    }

    private async Task<IList<long>> ReopenExhaustedRetryableEventsAsync(
        CancellationToken cancellationToken
    )
    {
        var exhaustedEvents = await _context.SyncEvents
            .Where(
                syncEvent =>
                    (
                        syncEvent.Status == SyncEventStatuses.Pending
                        || syncEvent.Status == SyncEventStatuses.FailedRetryable
                        || syncEvent.Status == SyncEventStatuses.Failed
                    )
                    && syncEvent.Attempts >= MaxAttempts
            )
            .OrderBy(syncEvent => syncEvent.CreatedAt)
            .ToListAsync(cancellationToken);

        var repairedAt = DateTimeOffset.UtcNow;

        foreach (var syncEvent in exhaustedEvents)
        {
            syncEvent.Status = SyncEventStatuses.Pending;
            syncEvent.Attempts = 0;
            syncEvent.LastError = null;
            syncEvent.ProcessedAt = null;
        }

        if (exhaustedEvents.Count > 0)
        {
            _logger.LogInformation(
                "Manual sync repair reopened {SyncEventCount} exhausted retryable sync events at {RepairedAt}.",
                exhaustedEvents.Count,
                repairedAt
            );
            await _context.SaveChangesAsync(cancellationToken);
        }

        return exhaustedEvents.Select(syncEvent => syncEvent.Id).ToList();
    }

    private async Task<IList<SyncEvent>> CreateMissingTodoListCreateEventsAsync(
        CancellationToken cancellationToken
    )
    {
        var todoLists = await _context.TodoList
            .IgnoreQueryFilters()
            .Where(todoList => !todoList.IsDeleted && todoList.ExternalId == null)
            .OrderBy(todoList => todoList.Id)
            .ToListAsync(cancellationToken);

        var syncEvents = new List<SyncEvent>();

        foreach (var todoList in todoLists)
        {
            if (await HasCreateEventAsync(SyncEntityTypes.TodoList, todoList.Id, cancellationToken))
            {
                continue;
            }

            syncEvents.Add(CreateSyncEvent(SyncEntityTypes.TodoList, todoList.Id, todoList));
        }

        return syncEvents;
    }

    private async Task<IList<SyncEvent>> CreateMissingItemCreateEventsAsync(
        CancellationToken cancellationToken
    )
    {
        var items = await _context.Items
            .IgnoreQueryFilters()
            .Where(item => !item.IsDeleted && item.ExternalId == null)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var syncEvents = new List<SyncEvent>();

        foreach (var item in items)
        {
            var todoList = await _context.TodoList
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == item.TodoListId,
                    cancellationToken
                );

            if (todoList == null || todoList.IsDeleted)
            {
                continue;
            }

            if (await HasCreateEventAsync(SyncEntityTypes.Item, item.Id, cancellationToken))
            {
                continue;
            }

            syncEvents.Add(CreateSyncEvent(SyncEntityTypes.Item, item.Id, item));
        }

        return syncEvents;
    }

    private Task<bool> HasCreateEventAsync(
        string entityType,
        long entityId,
        CancellationToken cancellationToken
    )
    {
        return _context.SyncEvents.AnyAsync(
            syncEvent =>
                syncEvent.EntityType == entityType
                && syncEvent.EntityId == entityId
                && syncEvent.EventType == SyncEventTypes.Created,
            cancellationToken
        );
    }

    private static SyncEvent CreateSyncEvent<TPayload>(
        string entityType,
        long entityId,
        TPayload payload
    )
    {
        return new SyncEvent
        {
            EntityType = entityType,
            EntityId = entityId,
            EventType = SyncEventTypes.Created,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = SyncEventStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
        };
    }
}

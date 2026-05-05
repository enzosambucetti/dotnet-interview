using System.Diagnostics;
using System.Text.Json;
using TodoApi.Models;

namespace TodoApi.Sync;

public class SyncEventPublisher : ISyncEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoContext _context;
    private readonly ISyncJobScheduler _syncJobScheduler;

    public SyncEventPublisher(TodoContext context, ISyncJobScheduler syncJobScheduler)
    {
        _context = context;
        _syncJobScheduler = syncJobScheduler;
    }

    public async Task PublishAsync<TPayload>(
        string entityType,
        long entityId,
        string eventType,
        TPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        var syncEvent = new SyncEvent
        {
            EntityType = entityType,
            EntityId = entityId,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = SyncEventStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
        };

        _context.SyncEvents.Add(syncEvent);
        await _context.SaveChangesAsync(cancellationToken);

        _syncJobScheduler.EnqueueOutboundSync(syncEvent.Id);
    }
}

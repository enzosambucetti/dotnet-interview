namespace TodoApi.Sync;

public class NoOpSyncEventPublisher : ISyncEventPublisher
{
    public Task PublishAsync<TPayload>(
        string entityType,
        long entityId,
        string eventType,
        TPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        return Task.CompletedTask;
    }
}

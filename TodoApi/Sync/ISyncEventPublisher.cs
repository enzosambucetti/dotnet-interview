namespace TodoApi.Sync;

public interface ISyncEventPublisher
{
    Task PublishAsync<TPayload>(
        string entityType,
        long entityId,
        string eventType,
        TPayload payload,
        CancellationToken cancellationToken = default
    );
}

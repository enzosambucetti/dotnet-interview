namespace TodoApi.Sync.Jobs;

public interface IOutboundSyncJob
{
    Task ProcessAsync(long syncEventId, CancellationToken cancellationToken);
}

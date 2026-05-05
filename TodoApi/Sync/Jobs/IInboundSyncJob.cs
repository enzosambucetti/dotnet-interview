namespace TodoApi.Sync.Jobs;

public interface IInboundSyncJob
{
    Task ProcessAsync(CancellationToken cancellationToken);
}

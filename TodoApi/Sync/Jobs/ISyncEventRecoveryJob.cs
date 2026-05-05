namespace TodoApi.Sync.Jobs;

public interface ISyncEventRecoveryJob
{
    Task ProcessAsync(CancellationToken cancellationToken);
}

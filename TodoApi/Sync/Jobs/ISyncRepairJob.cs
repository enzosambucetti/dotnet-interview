namespace TodoApi.Sync.Jobs;

public interface ISyncRepairJob
{
    Task ProcessAsync(CancellationToken cancellationToken);
}

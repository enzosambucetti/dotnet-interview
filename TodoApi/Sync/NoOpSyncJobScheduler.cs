namespace TodoApi.Sync;

public class NoOpSyncJobScheduler : ISyncJobScheduler
{
    public void EnqueueOutboundSync(long syncEventId)
    {
    }
}

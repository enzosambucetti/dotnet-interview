namespace TodoApi.Sync;

public interface ISyncJobScheduler
{
    void EnqueueOutboundSync(long syncEventId);
}

using Hangfire;
using TodoApi.Sync.Jobs;

namespace TodoApi.Sync;

public class HangfireSyncJobScheduler : ISyncJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireSyncJobScheduler(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public void EnqueueOutboundSync(long syncEventId)
    {
        _backgroundJobClient.Enqueue<IOutboundSyncJob>(
            job => job.ProcessAsync(syncEventId, CancellationToken.None)
        );
    }
}

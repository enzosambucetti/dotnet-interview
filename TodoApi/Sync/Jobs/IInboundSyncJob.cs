using Hangfire;

namespace TodoApi.Sync.Jobs;

public interface IInboundSyncJob
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    Task ProcessAsync(CancellationToken cancellationToken);
}

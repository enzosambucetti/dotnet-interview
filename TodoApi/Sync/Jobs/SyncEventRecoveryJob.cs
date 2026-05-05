using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace TodoApi.Sync.Jobs;

public class SyncEventRecoveryJob : ISyncEventRecoveryJob
{
    private const int MaxAttempts = 5;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly TodoContext _context;

    public SyncEventRecoveryJob(TodoContext context, IBackgroundJobClient backgroundJobClient)
    {
        _context = context;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var syncEvents = await _context.SyncEvents
            .Where(
                x =>
                    (
                        x.Status == SyncEventStatuses.Pending
                        || x.Status == SyncEventStatuses.FailedRetryable
                        || x.Status == SyncEventStatuses.Failed
                    )
                    && x.Attempts < MaxAttempts
            )
            .OrderBy(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var syncEvent in syncEvents)
        {
            _backgroundJobClient.Enqueue<IOutboundSyncJob>(
                job => job.ProcessAsync(syncEvent.Id, CancellationToken.None)
            );
        }
    }
}

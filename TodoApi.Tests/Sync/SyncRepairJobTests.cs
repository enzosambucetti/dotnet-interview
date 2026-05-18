using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.Models;
using TodoApi.Sync;
using TodoApi.Sync.Jobs;

namespace TodoApi.Tests.Sync;

public class SyncRepairJobTests
{
    [Fact]
    public async Task ProcessAsync_WhenRetryableEventReachedMaxAttempts_ReopensAndEnqueues()
    {
        using var context = CreateContext();
        context.SyncEvents.Add(
            new SyncEvent
            {
                EntityType = SyncEntityTypes.TodoList,
                EntityId = 6,
                EventType = SyncEventTypes.Created,
                PayloadJson = "{}",
                Status = SyncEventStatuses.FailedRetryable,
                Attempts = 5,
                LastError = "ExternalApi unavailable.",
                CreatedAt = DateTimeOffset.UtcNow,
                ProcessedAt = DateTimeOffset.UtcNow,
                CorrelationId = "test-correlation",
            }
        );
        await context.SaveChangesAsync();
        var scheduler = new FakeSyncJobScheduler();
        var job = CreateJob(context, scheduler);

        await job.ProcessAsync(CancellationToken.None);

        var syncEvent = await context.SyncEvents.SingleAsync();
        Assert.Equal(SyncEventStatuses.Pending, syncEvent.Status);
        Assert.Equal(0, syncEvent.Attempts);
        Assert.Null(syncEvent.LastError);
        Assert.Null(syncEvent.ProcessedAt);
        Assert.Equal(syncEvent.Id, scheduler.EnqueuedSyncEventIds.Single());
    }

    [Fact]
    public async Task ProcessAsync_WhenActiveTodoListHasNoExternalIdAndNoCreateEvent_CreatesAndEnqueues()
    {
        using var context = CreateContext();
        context.TodoList.Add(
            new TodoList
            {
                Id = 1,
                Name = "Test",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        );
        await context.SaveChangesAsync();
        var scheduler = new FakeSyncJobScheduler();
        var job = CreateJob(context, scheduler);

        await job.ProcessAsync(CancellationToken.None);

        var syncEvent = await context.SyncEvents.SingleAsync();
        Assert.Equal(SyncEntityTypes.TodoList, syncEvent.EntityType);
        Assert.Equal(1, syncEvent.EntityId);
        Assert.Equal(SyncEventTypes.Created, syncEvent.EventType);
        Assert.Equal(SyncEventStatuses.Pending, syncEvent.Status);
        Assert.Equal(syncEvent.Id, scheduler.EnqueuedSyncEventIds.Single());
    }

    [Fact]
    public async Task ProcessAsync_WhenActiveItemHasNoExternalIdAndNoCreateEvent_CreatesAndEnqueues()
    {
        using var context = CreateContext();
        context.TodoList.Add(
            new TodoList
            {
                Id = 1,
                ExternalId = "ext-list-001",
                Name = "Work",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        );
        context.Items.Add(
            new Item
            {
                Id = 10,
                TodoListId = 1,
                Name = "Review sync",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        );
        await context.SaveChangesAsync();
        var scheduler = new FakeSyncJobScheduler();
        var job = CreateJob(context, scheduler);

        await job.ProcessAsync(CancellationToken.None);

        var syncEvent = await context.SyncEvents.SingleAsync();
        Assert.Equal(SyncEntityTypes.Item, syncEvent.EntityType);
        Assert.Equal(10, syncEvent.EntityId);
        Assert.Equal(SyncEventTypes.Created, syncEvent.EventType);
        Assert.Equal(SyncEventStatuses.Pending, syncEvent.Status);
        Assert.Equal(syncEvent.Id, scheduler.EnqueuedSyncEventIds.Single());
    }

    [Fact]
    public async Task ProcessAsync_WhenCreatedEventAlreadyExists_DoesNotCreateDuplicate()
    {
        using var context = CreateContext();
        context.TodoList.Add(
            new TodoList
            {
                Id = 1,
                Name = "Already tracked",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        );
        context.SyncEvents.Add(
            new SyncEvent
            {
                EntityType = SyncEntityTypes.TodoList,
                EntityId = 1,
                EventType = SyncEventTypes.Created,
                PayloadJson = "{}",
                Status = SyncEventStatuses.FailedTerminal,
                Attempts = 5,
                CreatedAt = DateTimeOffset.UtcNow,
                CorrelationId = "test-correlation",
            }
        );
        await context.SaveChangesAsync();
        var scheduler = new FakeSyncJobScheduler();
        var job = CreateJob(context, scheduler);

        await job.ProcessAsync(CancellationToken.None);

        Assert.Equal(1, await context.SyncEvents.CountAsync());
        Assert.Empty(scheduler.EnqueuedSyncEventIds);
    }

    [Fact]
    public async Task ProcessAsync_WhenInboundReconciliationLinksEntity_DoesNotCreateCreateEvent()
    {
        using var context = CreateContext();
        context.TodoList.Add(
            new TodoList
            {
                Id = 1,
                SourceId = "local-list-1",
                Name = "Already external",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        );
        await context.SaveChangesAsync();
        var scheduler = new FakeSyncJobScheduler();
        var inboundJob = new FakeInboundSyncJob(async cancellationToken =>
        {
            var todoList = await context.TodoList.SingleAsync(cancellationToken);
            todoList.ExternalId = "ext-list-001";
            await context.SaveChangesAsync(cancellationToken);
        });
        var job = CreateJob(context, scheduler, inboundJob);

        await job.ProcessAsync(CancellationToken.None);

        Assert.Equal(1, inboundJob.CallCount);
        Assert.Empty(context.SyncEvents);
        Assert.Empty(scheduler.EnqueuedSyncEventIds);
    }

    private static SyncRepairJob CreateJob(
        TodoContext context,
        FakeSyncJobScheduler scheduler,
        IInboundSyncJob? inboundSyncJob = null
    )
    {
        return new SyncRepairJob(
            context,
            inboundSyncJob ?? new FakeInboundSyncJob(),
            scheduler,
            NullLogger<SyncRepairJob>.Instance
        );
    }

    private static TodoContext CreateContext()
    {
        return new TodoContext(
            new DbContextOptionsBuilder<TodoContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
    }

    private class FakeSyncJobScheduler : ISyncJobScheduler
    {
        public List<long> EnqueuedSyncEventIds { get; } = new();

        public void EnqueueOutboundSync(long syncEventId)
        {
            EnqueuedSyncEventIds.Add(syncEventId);
        }
    }

    private class FakeInboundSyncJob : IInboundSyncJob
    {
        private readonly Func<CancellationToken, Task>? _processAsync;

        public FakeInboundSyncJob(Func<CancellationToken, Task>? processAsync = null)
        {
            _processAsync = processAsync;
        }

        public int CallCount { get; private set; }

        public async Task ProcessAsync(CancellationToken cancellationToken)
        {
            CallCount++;

            if (_processAsync != null)
            {
                await _processAsync(cancellationToken);
            }
        }
    }
}

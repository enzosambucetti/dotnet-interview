using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.External;
using TodoApi.Models;
using TodoApi.Sync;
using TodoApi.Sync.Jobs;

namespace TodoApi.Tests.Sync;

public class OutboundSyncJobTests
{
    [Fact]
    public async Task ProcessAsync_WhenTodoListCreateSucceeds_UpdatesExternalIdAndCompletesEvent()
    {
        using var context = CreateContext();
        var todoList = new TodoList { Name = "Work" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.TodoList, todoList.Id, SyncEventTypes.Created);
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient
        {
            CreatedTodoList = new ExternalTodoList
            {
                Id = "ext-list-1",
                SourceId = todoList.Id.ToString(),
                Name = "Work",
            },
        };
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal("ext-list-1", todoList.ExternalId);
        Assert.Equal(SyncEventStatuses.Completed, syncEvent.Status);
        Assert.Equal(1, syncEvent.Attempts);
        Assert.NotNull(syncEvent.ProcessedAt);
    }

    [Fact]
    public async Task ProcessAsync_WhenSyncFails_IncrementsAttemptsAndStoresLastError()
    {
        using var context = CreateContext();
        var todoList = new TodoList { Name = "Work", ExternalId = "ext-list-1" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();
        var item = new Item { TodoListId = todoList.Id, Name = "Review sync" };
        context.Items.Add(item);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.Item, item.Id, SyncEventTypes.Updated);
        await context.SaveChangesAsync();
        var job = CreateJob(context, new FakeExternalTodoApiClient());

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal(SyncEventStatuses.FailedTerminal, syncEvent.Status);
        Assert.Equal(1, syncEvent.Attempts);
        Assert.Contains("ExternalId", syncEvent.LastError);
    }

    [Fact]
    public async Task ProcessAsync_WhenExternalDeleteReturns404_CompletesAsIdempotentSuccess()
    {
        using var context = CreateContext();
        var todoList = new TodoList { Name = "Work", ExternalId = "ext-list-1", IsDeleted = true };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.TodoList, todoList.Id, SyncEventTypes.Deleted);
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient { DeleteTodoListThrowsNotFound = true };
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal(SyncEventStatuses.Completed, syncEvent.Status);
        Assert.Equal(1, syncEvent.Attempts);
        Assert.NotNull(syncEvent.ProcessedAt);
    }

    [Fact]
    public async Task ProcessAsync_WhenItemCreateReturns404AndInboundConfirmsParentDeleted_CompletesEvent()
    {
        using var context = CreateContext();
        var todoList = new TodoList { Name = "Work", ExternalId = "ext-list-1" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();
        var item = new Item { TodoListId = todoList.Id, Name = "Review sync" };
        context.Items.Add(item);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.Item, item.Id, SyncEventTypes.Created);
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient { CreateTodoItemThrowsNotFound = true };
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal(SyncEventStatuses.Completed, syncEvent.Status);
        Assert.Equal(1, syncEvent.Attempts);
        Assert.Null(syncEvent.LastError);
        Assert.NotNull(syncEvent.ProcessedAt);
        Assert.Equal(1, externalClient.ListTodoListsCalls);
        Assert.True(todoList.IsDeleted);
        Assert.True(item.IsDeleted);
    }

    [Fact]
    public async Task ProcessAsync_WhenItemCreateReturns404ButInboundDoesNotConfirmDeletion_FailsEvent()
    {
        using var context = CreateContext();
        var todoList = new TodoList { Name = "Work", ExternalId = "ext-list-1" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();
        var item = new Item { TodoListId = todoList.Id, Name = "Review sync" };
        context.Items.Add(item);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.Item, item.Id, SyncEventTypes.Created);
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient
        {
            CreateTodoItemThrowsNotFound = true,
            TodoLists =
            [
                new ExternalTodoList
                {
                    Id = "ext-list-1",
                    SourceId = todoList.SourceId,
                    Name = todoList.Name,
                    CreatedAt = todoList.CreatedAt,
                    UpdatedAt = todoList.UpdatedAt,
                },
            ],
        };
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal(SyncEventStatuses.FailedRetryable, syncEvent.Status);
        Assert.Equal(1, syncEvent.Attempts);
        Assert.Contains("Not found", syncEvent.LastError);
        Assert.Equal(1, externalClient.ListTodoListsCalls);
        Assert.False(todoList.IsDeleted);
        Assert.False(item.IsDeleted);
    }

    [Fact]
    public async Task ProcessAsync_WhenItemCreateWaitsForParentExternalId_RemainsPendingWithoutConsumingAttempt()
    {
        using var context = CreateContext();
        var todoList = new TodoList { Name = "Work" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();
        var item = new Item { TodoListId = todoList.Id, Name = "Review sync" };
        context.Items.Add(item);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.Item, item.Id, SyncEventTypes.Created);
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient();
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal(SyncEventStatuses.Pending, syncEvent.Status);
        Assert.Equal(0, syncEvent.Attempts);
        Assert.Contains("does not have ExternalId", syncEvent.LastError);
        Assert.Equal(0, externalClient.CreateTodoItemCalls);
    }

    [Fact]
    public async Task ProcessAsync_WhenCreateEventIsObsoleteBecauseEntityWasSoftDeleted_CompletesWithoutCallingExternalApi()
    {
        using var context = CreateContext();
        var todoList = new TodoList { Name = "Work", ExternalId = "ext-list-1" };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();
        var item = new Item
        {
            TodoListId = todoList.Id,
            Name = "Review sync",
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow,
        };
        context.Items.Add(item);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.Item, item.Id, SyncEventTypes.Created);
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient();
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal(SyncEventStatuses.Completed, syncEvent.Status);
        Assert.Equal(0, syncEvent.Attempts);
        Assert.Equal(0, externalClient.CreateTodoItemCalls);
    }

    [Fact]
    public async Task ProcessAsync_WhenTodoListCreateTimesOutButInboundFindsSourceId_CompletesAndLinksExternalId()
    {
        using var context = CreateContext();
        var todoList = new TodoList
        {
            Name = "Work",
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.TodoList, todoList.Id, SyncEventTypes.Created);
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient
        {
            CreateTodoListThrowsTimeout = true,
            TodoLists =
            [
                new ExternalTodoList
                {
                    Id = "ext-list-1",
                    SourceId = todoList.Id.ToString(),
                    Name = "Work",
                    CreatedAt = todoList.CreatedAt,
                    UpdatedAt = todoList.UpdatedAt,
                },
            ],
        };
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal(SyncEventStatuses.Completed, syncEvent.Status);
        Assert.Equal("ext-list-1", todoList.ExternalId);
        Assert.Equal(1, await context.TodoList.CountAsync());
        Assert.Equal(1, externalClient.ListTodoListsCalls);
    }

    [Fact]
    public async Task ProcessAsync_WhenDeletedItemDoesNotHaveExternalId_CompletesWithoutCallingExternalApi()
    {
        using var context = CreateContext();
        var item = new Item
        {
            TodoListId = 1,
            Name = "Review sync",
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow,
        };
        context.Items.Add(item);
        await context.SaveChangesAsync();
        var syncEvent = AddSyncEvent(context, SyncEntityTypes.Item, item.Id, SyncEventTypes.Deleted);
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient();
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(syncEvent.Id, CancellationToken.None);

        Assert.Equal(SyncEventStatuses.Completed, syncEvent.Status);
        Assert.Equal(1, syncEvent.Attempts);
        Assert.Equal(0, externalClient.DeleteTodoItemCalls);
    }

    private static OutboundSyncJob CreateJob(
        TodoContext context,
        IExternalTodoApiClient externalTodoApiClient
    )
    {
        var inboundSyncJob = new InboundSyncJob(
            context,
            externalTodoApiClient,
            NullLogger<InboundSyncJob>.Instance
        );

        return new OutboundSyncJob(
            context,
            externalTodoApiClient,
            inboundSyncJob,
            NullLogger<OutboundSyncJob>.Instance
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

    private static SyncEvent AddSyncEvent(
        TodoContext context,
        string entityType,
        long entityId,
        string eventType
    )
    {
        var syncEvent = new SyncEvent
        {
            EntityType = entityType,
            EntityId = entityId,
            EventType = eventType,
            PayloadJson = "{}",
            Status = SyncEventStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            CorrelationId = "test",
        };
        context.SyncEvents.Add(syncEvent);
        return syncEvent;
    }

    private class FakeExternalTodoApiClient : IExternalTodoApiClient
    {
        public ExternalTodoList? CreatedTodoList { get; set; }
        public bool DeleteTodoListThrowsNotFound { get; set; }
        public bool CreateTodoItemThrowsNotFound { get; set; }
        public bool CreateTodoListThrowsTimeout { get; set; }
        public int ListTodoListsCalls { get; private set; }
        public int CreateTodoItemCalls { get; private set; }
        public int DeleteTodoItemCalls { get; private set; }
        public IList<ExternalTodoList> TodoLists { get; set; } = new List<ExternalTodoList>();

        public Task<IList<ExternalTodoList>> ListTodoListsAsync(CancellationToken cancellationToken)
        {
            ListTodoListsCalls++;
            return Task.FromResult(TodoLists);
        }

        public Task<ExternalTodoList> CreateTodoListAsync(
            CreateExternalTodoListRequest request,
            CancellationToken cancellationToken
        )
        {
            if (CreateTodoListThrowsTimeout)
            {
                throw new TaskCanceledException("Create TodoList timed out.");
            }

            return Task.FromResult(CreatedTodoList!);
        }

        public Task<ExternalTodoList> UpdateTodoListAsync(
            string todoListId,
            UpdateExternalTodoListRequest request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new ExternalTodoList { Id = todoListId, Name = request.Name });
        }

        public Task DeleteTodoListAsync(string todoListId, CancellationToken cancellationToken)
        {
            if (DeleteTodoListThrowsNotFound)
            {
                throw new ExternalApiException(HttpStatusCode.NotFound, "Not found");
            }

            return Task.CompletedTask;
        }

        public Task<ExternalTodoItem> CreateTodoItemAsync(
            string todoListId,
            CreateExternalTodoItemRequest request,
            CancellationToken cancellationToken
        )
        {
            CreateTodoItemCalls++;

            if (CreateTodoItemThrowsNotFound)
            {
                throw new ExternalApiException(HttpStatusCode.NotFound, "Not found");
            }

            return Task.FromResult(new ExternalTodoItem { Id = "ext-item-1" });
        }

        public Task<ExternalTodoItem> UpdateTodoItemAsync(
            string todoListId,
            string todoItemId,
            UpdateExternalTodoItemRequest request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new ExternalTodoItem { Id = todoItemId });
        }

        public Task DeleteTodoItemAsync(
            string todoListId,
            string todoItemId,
            CancellationToken cancellationToken
        )
        {
            DeleteTodoItemCalls++;
            return Task.CompletedTask;
        }
    }
}

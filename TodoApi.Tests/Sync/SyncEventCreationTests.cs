using Microsoft.EntityFrameworkCore;
using TodoApi.Dtos;
using TodoApi.Repositories;
using TodoApi.Services;
using TodoApi.Sync;

namespace TodoApi.Tests.Sync;

public class SyncEventCreationTests
{
    [Fact]
    public async Task TodoListMutation_WhenCreated_CreatesSyncEventAndEnqueuesJob()
    {
        using var context = CreateContext();
        var scheduler = new FakeSyncJobScheduler();
        var service = new TodoListsService(
            new TodoListsRepository(context),
            new SyncEventPublisher(context, scheduler)
        );

        var todoList = await service.CreateTodoListAsync(new CreateTodoList { Name = "Work" });

        var syncEvent = await context.SyncEvents.SingleAsync();
        Assert.Equal(SyncEntityTypes.TodoList, syncEvent.EntityType);
        Assert.Equal(todoList.Id, syncEvent.EntityId);
        Assert.Equal(SyncEventTypes.Created, syncEvent.EventType);
        Assert.Equal(SyncEventStatuses.Pending, syncEvent.Status);
        Assert.Equal(syncEvent.Id, scheduler.EnqueuedSyncEventIds.Single());
    }

    [Fact]
    public async Task ItemMutation_WhenCreated_CreatesSyncEventAndEnqueuesJob()
    {
        using var context = CreateContext();
        var scheduler = new FakeSyncJobScheduler();
        context.TodoList.Add(new() { Id = 1, Name = "Work" });
        await context.SaveChangesAsync();
        var service = new ItemsService(
            new ItemsRepository(context),
            new SyncEventPublisher(context, scheduler)
        );

        var item = await service.CreateItemAsync(
            1,
            new CreateItem { Name = "Review sync", IsCompleted = false }
        );

        var syncEvent = await context.SyncEvents.SingleAsync();
        Assert.Equal(SyncEntityTypes.Item, syncEvent.EntityType);
        Assert.Equal(item!.Id, syncEvent.EntityId);
        Assert.Equal(SyncEventTypes.Created, syncEvent.EventType);
        Assert.Equal(SyncEventStatuses.Pending, syncEvent.Status);
        Assert.Equal(syncEvent.Id, scheduler.EnqueuedSyncEventIds.Single());
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
}

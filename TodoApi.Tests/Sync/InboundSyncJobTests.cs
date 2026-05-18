using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.External;
using TodoApi.Models;
using TodoApi.Realtime;
using TodoApi.Sync.Jobs;

namespace TodoApi.Tests.Sync;

public class InboundSyncJobTests
{
    [Fact]
    public async Task ProcessAsync_WhenExternalDataExists_ImportsAndUpdatesWithoutOutboundEvents()
    {
        using var context = CreateContext();
        var externalClient = new FakeExternalTodoApiClient
        {
            TodoLists = new List<ExternalTodoList>
            {
                new()
                {
                    Id = "ext-list-1",
                    SourceId = "external-seed",
                    Name = "External Work",
                    CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    Items = new List<ExternalTodoItem>
                    {
                        new()
                        {
                            Id = "ext-item-1",
                            SourceId = "external-item-seed",
                            Description = "Review sync",
                            Completed = false,
                            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                            UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                        },
                    },
                },
            },
        };
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(CancellationToken.None);
        externalClient.TodoLists[0].Name = "External Work Updated";
        externalClient.TodoLists[0].UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        await job.ProcessAsync(CancellationToken.None);

        var todoList = await context.TodoList.SingleAsync();
        var item = await context.Items.SingleAsync();
        Assert.Equal("External Work Updated", todoList.Name);
        Assert.Equal("ext-list-1", todoList.ExternalId);
        Assert.Equal("ext-item-1", item.ExternalId);
        Assert.Empty(context.SyncEvents);
    }

    [Fact]
    public async Task ProcessAsync_WhenInboundChangesData_PublishesRealtimeEvents()
    {
        using var context = CreateContext();
        var notifier = new FakeTodoRealtimeNotifier();
        var externalClient = new FakeExternalTodoApiClient
        {
            TodoLists = new List<ExternalTodoList>
            {
                new()
                {
                    Id = "ext-list-1",
                    SourceId = "external-seed",
                    Name = "External Work",
                    CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    Items = new List<ExternalTodoItem>
                    {
                        new()
                        {
                            Id = "ext-item-1",
                            SourceId = "external-item-seed",
                            Description = "Review sync",
                            Completed = false,
                            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                            UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                        },
                    },
                },
            },
        };
        var job = CreateJob(context, externalClient, notifier);

        await job.ProcessAsync(CancellationToken.None);

        Assert.Contains(notifier.Events, x => x.EventType == TodoRealtimeEventTypes.TodoListCreated);
        Assert.Contains(notifier.Events, x => x.EventType == TodoRealtimeEventTypes.ItemCreated);
        Assert.Contains(notifier.Events, x => x.EventType == TodoRealtimeEventTypes.InboundSyncCompleted);
        Assert.All(notifier.Events, x => Assert.Equal(TodoRealtimeSources.InboundSync, x.Source));
    }

    [Fact]
    public async Task ProcessAsync_WhenPreviouslySeenExternalDataDisappears_SoftDeletesLocalData()
    {
        using var context = CreateContext();
        context.TodoList.Add(
            new TodoList
            {
                Id = 1,
                ExternalId = "ext-list-1",
                Name = "External Work",
                CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            }
        );
        context.Items.Add(
            new Item
            {
                Id = 1,
                ExternalId = "ext-item-1",
                TodoListId = 1,
                Name = "Review sync",
                CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            }
        );
        await context.SaveChangesAsync();
        var job = CreateJob(context, new FakeExternalTodoApiClient());

        await job.ProcessAsync(CancellationToken.None);

        var todoList = await context.TodoList.IgnoreQueryFilters().SingleAsync();
        var item = await context.Items.IgnoreQueryFilters().SingleAsync();
        Assert.True(todoList.IsDeleted);
        Assert.True(item.IsDeleted);
        Assert.NotNull(todoList.DeletedAt);
        Assert.NotNull(item.DeletedAt);
    }

    [Fact]
    public async Task ProcessAsync_WhenExternalDataIsNewerThanLocalSoftDelete_RestoresLocalData()
    {
        using var context = CreateContext();
        var deletedAt = DateTimeOffset.Parse("2026-01-01T00:10:00Z");
        var externalCreatedAt = DateTimeOffset.Parse("2026-01-01T00:11:00Z");
        context.TodoList.Add(
            new TodoList
            {
                Id = 1,
                ExternalId = "ext-list-1",
                SourceId = "old-source",
                Name = "Old deleted list",
                CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                UpdatedAt = deletedAt,
                IsDeleted = true,
                DeletedAt = deletedAt,
            }
        );
        context.Items.Add(
            new Item
            {
                Id = 1,
                ExternalId = "ext-item-1",
                SourceId = "old-item-source",
                TodoListId = 1,
                Name = "Old deleted item",
                CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                UpdatedAt = deletedAt,
                IsDeleted = true,
                DeletedAt = deletedAt,
            }
        );
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient
        {
            TodoLists = new List<ExternalTodoList>
            {
                new()
                {
                    Id = "ext-list-1",
                    SourceId = null,
                    Name = "external para inbound",
                    CreatedAt = externalCreatedAt,
                    UpdatedAt = externalCreatedAt,
                    Items = new List<ExternalTodoItem>
                    {
                        new()
                        {
                            Id = "ext-item-1",
                            SourceId = null,
                            Description = "external para inbound",
                            Completed = false,
                            CreatedAt = externalCreatedAt,
                            UpdatedAt = externalCreatedAt,
                        },
                    },
                },
            },
        };
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(CancellationToken.None);

        var todoList = await context.TodoList.SingleAsync();
        var item = await context.Items.SingleAsync();
        Assert.False(todoList.IsDeleted);
        Assert.Null(todoList.DeletedAt);
        Assert.Null(todoList.SourceId);
        Assert.Equal("external para inbound", todoList.Name);
        Assert.Equal(externalCreatedAt, todoList.CreatedAt);
        Assert.Equal(externalCreatedAt, todoList.UpdatedAt);
        Assert.False(item.IsDeleted);
        Assert.Null(item.DeletedAt);
        Assert.Null(item.SourceId);
        Assert.Equal("external para inbound", item.Name);
        Assert.Equal(externalCreatedAt, item.CreatedAt);
        Assert.Equal(externalCreatedAt, item.UpdatedAt);
        Assert.Empty(context.SyncEvents);
    }

    [Fact]
    public async Task ProcessAsync_WhenLocalSoftDeleteIsNewerThanExternalCreate_DoesNotRestoreOrImportItems()
    {
        using var context = CreateContext();
        var externalCreatedAt = DateTimeOffset.Parse("2026-01-01T00:11:00Z");
        context.TodoList.Add(
            new TodoList
            {
                Id = 1,
                ExternalId = "ext-list-1",
                Name = "Local deleted after external create",
                CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:12:00Z"),
                IsDeleted = true,
                DeletedAt = DateTimeOffset.Parse("2026-01-01T00:12:00Z"),
            }
        );
        await context.SaveChangesAsync();
        var externalClient = new FakeExternalTodoApiClient
        {
            TodoLists = new List<ExternalTodoList>
            {
                new()
                {
                    Id = "ext-list-1",
                    Name = "External still visible before outbound delete",
                    CreatedAt = externalCreatedAt,
                    UpdatedAt = externalCreatedAt,
                    Items = new List<ExternalTodoItem>
                    {
                        new()
                        {
                            Id = "ext-item-1",
                            Description = "Should not be imported under deleted parent",
                            Completed = false,
                            CreatedAt = externalCreatedAt,
                            UpdatedAt = externalCreatedAt,
                        },
                    },
                },
            },
        };
        var job = CreateJob(context, externalClient);

        await job.ProcessAsync(CancellationToken.None);

        Assert.Empty(await context.TodoList.ToListAsync());
        Assert.Empty(await context.Items.ToListAsync());
        var deletedList = await context.TodoList.IgnoreQueryFilters().SingleAsync();
        Assert.True(deletedList.IsDeleted);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:12:00Z"), deletedList.DeletedAt);
    }

    private static InboundSyncJob CreateJob(
        TodoContext context,
        IExternalTodoApiClient externalTodoApiClient,
        ITodoRealtimeNotifier? todoRealtimeNotifier = null
    )
    {
        return new InboundSyncJob(
            context,
            externalTodoApiClient,
            NullLogger<InboundSyncJob>.Instance,
            todoRealtimeNotifier
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

    private class FakeExternalTodoApiClient : IExternalTodoApiClient
    {
        public IList<ExternalTodoList> TodoLists { get; set; } = new List<ExternalTodoList>();

        public Task<IList<ExternalTodoList>> ListTodoListsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TodoLists);
        }

        public Task<ExternalTodoList> CreateTodoListAsync(
            CreateExternalTodoListRequest request,
            CancellationToken cancellationToken
        )
        {
            throw new NotImplementedException();
        }

        public Task<ExternalTodoList> UpdateTodoListAsync(
            string todoListId,
            UpdateExternalTodoListRequest request,
            CancellationToken cancellationToken
        )
        {
            throw new NotImplementedException();
        }

        public Task DeleteTodoListAsync(string todoListId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ExternalTodoItem> CreateTodoItemAsync(
            string todoListId,
            CreateExternalTodoItemRequest request,
            CancellationToken cancellationToken
        )
        {
            throw new NotImplementedException();
        }

        public Task<ExternalTodoItem> UpdateTodoItemAsync(
            string todoListId,
            string todoItemId,
            UpdateExternalTodoItemRequest request,
            CancellationToken cancellationToken
        )
        {
            throw new NotImplementedException();
        }

        public Task DeleteTodoItemAsync(
            string todoListId,
            string todoItemId,
            CancellationToken cancellationToken
        )
        {
            throw new NotImplementedException();
        }
    }

    private class FakeTodoRealtimeNotifier : ITodoRealtimeNotifier
    {
        public List<TodoRealtimeEvent> Events { get; } = new();

        public Task PublishAsync(
            TodoRealtimeEvent realtimeEvent,
            CancellationToken cancellationToken = default
        )
        {
            Events.Add(realtimeEvent);
            return Task.CompletedTask;
        }

        public Task PublishManyAsync(
            IEnumerable<TodoRealtimeEvent> realtimeEvents,
            CancellationToken cancellationToken = default
        )
        {
            Events.AddRange(realtimeEvents);
            return Task.CompletedTask;
        }
    }
}

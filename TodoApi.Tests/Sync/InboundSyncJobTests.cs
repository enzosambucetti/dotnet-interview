using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.External;
using TodoApi.Models;
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

    private static InboundSyncJob CreateJob(
        TodoContext context,
        IExternalTodoApiClient externalTodoApiClient
    )
    {
        return new InboundSyncJob(
            context,
            externalTodoApiClient,
            NullLogger<InboundSyncJob>.Instance
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
}

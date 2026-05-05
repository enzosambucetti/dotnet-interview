using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.Controllers;
using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;
using TodoApi.Services;

namespace TodoApi.Tests.Integration;

public class ItemsIntegrationTests
{
    [Fact]
    public async Task ItemsFlow_UsingControllerServiceAndMockedRepository_CreatesUpdatesAndSoftDeletes()
    {
        var repository = new FakeItemsRepository(existingTodoListIds: new[] { 10L });
        var controller = new ItemsController(
            new ItemsService(repository),
            NullLogger<ItemsController>.Instance
        );
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var updatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z");

        var createResult = await controller.PostItem(
            10,
            new CreateItem
            {
                SourceId = "ext-item-1",
                Name = "Review sync",
                IsCompleted = false,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            }
        );
        var created = (createResult.Result as CreatedAtActionResult)?.Value as Item;

        var updateResult = await controller.PutItem(
            10,
            created!.Id,
            new UpdateItem
            {
                SourceId = "ext-item-1",
                Name = "Review sync implementation",
                IsCompleted = true,
                UpdatedAt = updatedAt,
            }
        );

        var deleteResult = await controller.DeleteItem(10, created.Id);
        var getDeletedResult = await controller.GetItem(10, created.Id);

        Assert.IsType<CreatedAtActionResult>(createResult.Result);
        Assert.IsType<OkObjectResult>(updateResult);
        Assert.IsType<NoContentResult>(deleteResult);
        var problem = Assert.IsType<ObjectResult>(getDeletedResult.Result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("Review sync implementation", created.Name);
        Assert.True(created.IsCompleted);
        Assert.True(created.IsDeleted);
        Assert.NotNull(created.DeletedAt);
    }

    private class FakeItemsRepository : IItemsRepository
    {
        private readonly HashSet<long> _existingTodoListIds;
        private readonly List<Item> _items = new();
        private long _nextId = 1;

        public FakeItemsRepository(IEnumerable<long> existingTodoListIds)
        {
            _existingTodoListIds = existingTodoListIds.ToHashSet();
        }

        public Task<bool> TodoListExistsAsync(long todoListId)
        {
            return Task.FromResult(_existingTodoListIds.Contains(todoListId));
        }

        public Task<IList<Item>> GetItemsAsync(long todoListId)
        {
            return Task.FromResult<IList<Item>>(
                _items.Where(x => x.TodoListId == todoListId && !x.IsDeleted).ToList()
            );
        }

        public Task<Item?> GetItemAsync(long todoListId, long id)
        {
            return Task.FromResult(
                _items.FirstOrDefault(x => x.TodoListId == todoListId && x.Id == id && !x.IsDeleted)
            );
        }

        public Task<Item> AddItemAsync(Item item)
        {
            item.Id = _nextId++;
            _items.Add(item);
            return Task.FromResult(item);
        }

        public Task<Item> UpdateItemAsync(Item item)
        {
            return Task.FromResult(item);
        }

        public Task SoftDeleteItemAsync(Item item, DateTimeOffset deletedAt)
        {
            item.IsDeleted = true;
            item.DeletedAt = deletedAt;
            item.UpdatedAt = deletedAt;
            return Task.CompletedTask;
        }
    }
}

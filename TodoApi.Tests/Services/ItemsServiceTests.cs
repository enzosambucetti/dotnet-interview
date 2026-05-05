using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;
using TodoApi.Services;

namespace TodoApi.Tests.Services;

public class ItemsServiceTests
{
    [Fact]
    public async Task CreateItemAsync_WhenTodoListExists_PreservesSyncMetadata()
    {
        var repository = new FakeItemsRepository(todoListExists: true);
        var service = new ItemsService(repository);
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var updatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z");

        var item = await service.CreateItemAsync(
            10,
            new CreateItem
            {
                SourceId = "ext-item-1",
                Name = "Review sync",
                IsCompleted = true,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
            }
        );

        Assert.NotNull(item);
        Assert.Equal("ext-item-1", item.SourceId);
        Assert.Equal("Review sync", item.Name);
        Assert.True(item.IsCompleted);
        Assert.Equal(createdAt, item.CreatedAt);
        Assert.Equal(updatedAt, item.UpdatedAt);
    }

    [Fact]
    public async Task DeleteItemAsync_WhenItemExists_SoftDeletesItem()
    {
        var item = new Item { Id = 1, TodoListId = 10, Name = "Review sync" };
        var repository = new FakeItemsRepository(todoListExists: true, item);
        var service = new ItemsService(repository);

        var deleted = await service.DeleteItemAsync(10, 1);

        Assert.True(deleted);
        Assert.True(item.IsDeleted);
        Assert.NotNull(item.DeletedAt);
        Assert.Equal(item.DeletedAt, item.UpdatedAt);
    }

    private class FakeItemsRepository : IItemsRepository
    {
        private readonly List<Item> _items;
        private readonly bool _todoListExists;

        public FakeItemsRepository(bool todoListExists, params Item[] items)
        {
            _todoListExists = todoListExists;
            _items = items.ToList();
        }

        public Task<bool> TodoListExistsAsync(long todoListId)
        {
            return Task.FromResult(_todoListExists);
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
            item.Id = _items.Count + 1;
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

using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;
using TodoApi.Services;

namespace TodoApi.Tests.Services;

public class TodoListsServiceTests
{
    [Fact]
    public async Task CreateTodoListAsync_WhenSyncMetadataProvided_PreservesMetadata()
    {
        var repository = new FakeTodoListsRepository();
        var service = new TodoListsService(repository);
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var updatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z");

        var todoList = await service.CreateTodoListAsync(
            new CreateTodoList
            {
                SourceId = "ext-list-1",
                Name = "Work",
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
            }
        );

        Assert.Equal("ext-list-1", todoList.SourceId);
        Assert.Equal("Work", todoList.Name);
        Assert.Equal(createdAt, todoList.CreatedAt);
        Assert.Equal(updatedAt, todoList.UpdatedAt);
        Assert.False(todoList.IsDeleted);
        Assert.Null(todoList.DeletedAt);
    }

    [Fact]
    public async Task DeleteTodoListAsync_WhenTodoListExists_SoftDeletesTodoList()
    {
        var todoList = new TodoList { Id = 1, Name = "Work" };
        var repository = new FakeTodoListsRepository(todoList);
        var service = new TodoListsService(repository);

        var deleted = await service.DeleteTodoListAsync(1);

        Assert.True(deleted);
        Assert.True(todoList.IsDeleted);
        Assert.NotNull(todoList.DeletedAt);
        Assert.Equal(todoList.DeletedAt, todoList.UpdatedAt);
    }

    private class FakeTodoListsRepository : ITodoListsRepository
    {
        private readonly List<TodoList> _todoLists;

        public FakeTodoListsRepository(params TodoList[] todoLists)
        {
            _todoLists = todoLists.ToList();
        }

        public Task<IList<TodoList>> GetTodoListsAsync()
        {
            return Task.FromResult<IList<TodoList>>(_todoLists.Where(x => !x.IsDeleted).ToList());
        }

        public Task<TodoList?> GetTodoListAsync(long id)
        {
            return Task.FromResult(_todoLists.FirstOrDefault(x => x.Id == id && !x.IsDeleted));
        }

        public Task<TodoList> AddTodoListAsync(TodoList todoList)
        {
            todoList.Id = _todoLists.Count + 1;
            _todoLists.Add(todoList);
            return Task.FromResult(todoList);
        }

        public Task<TodoList> UpdateTodoListAsync(TodoList todoList)
        {
            return Task.FromResult(todoList);
        }

        public Task SoftDeleteTodoListAsync(TodoList todoList, DateTimeOffset deletedAt)
        {
            todoList.IsDeleted = true;
            todoList.DeletedAt = deletedAt;
            todoList.UpdatedAt = deletedAt;
            return Task.CompletedTask;
        }
    }
}

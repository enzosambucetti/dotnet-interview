using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.Controllers;
using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;
using TodoApi.Services;

namespace TodoApi.Tests.Integration;

public class TodoListsIntegrationTests
{
    [Fact]
    public async Task TodoListsFlow_UsingControllerServiceAndMockedRepository_CreatesUpdatesAndSoftDeletes()
    {
        var repository = new FakeTodoListsRepository();
        var controller = new TodoListsController(
            new TodoListsService(repository),
            NullLogger<TodoListsController>.Instance
        );
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var updatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z");

        var createResult = await controller.PostTodoList(
            new CreateTodoList
            {
                SourceId = "ext-list-1",
                Name = "Work",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            }
        );
        var created = (createResult.Result as CreatedAtActionResult)?.Value as TodoList;

        var updateResult = await controller.PutTodoList(
            created!.Id,
            new UpdateTodoList
            {
                SourceId = "ext-list-1",
                Name = "Updated Work",
                UpdatedAt = updatedAt,
            }
        );

        var deleteResult = await controller.DeleteTodoList(created.Id);
        var getDeletedResult = await controller.GetTodoList(created.Id);

        Assert.IsType<CreatedAtActionResult>(createResult.Result);
        Assert.IsType<OkObjectResult>(updateResult);
        Assert.IsType<NoContentResult>(deleteResult);
        var problem = Assert.IsType<ObjectResult>(getDeletedResult.Result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("Updated Work", created.Name);
        Assert.True(created.IsDeleted);
        Assert.NotNull(created.DeletedAt);
    }

    private class FakeTodoListsRepository : ITodoListsRepository
    {
        private readonly List<TodoList> _todoLists = new();
        private long _nextId = 1;

        public Task<IList<TodoList>> GetTodoListsAsync()
        {
            return Task.FromResult<IList<TodoList>>(_todoLists.Where(x => !x.IsDeleted).ToList());
        }

        public Task<TodoList?> GetTodoListAsync(long id)
        {
            return Task.FromResult(_todoLists.FirstOrDefault(x => x.Id == id && !x.IsDeleted));
        }

        public Task<TodoList?> GetTodoListWithItemsAsync(long id)
        {
            return GetTodoListAsync(id);
        }

        public Task<TodoList> AddTodoListAsync(TodoList todoList)
        {
            todoList.Id = _nextId++;
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

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.Controllers;
using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;
using TodoApi.Services;

namespace TodoApi.Tests;

#nullable disable
public class TodoListsControllerTests
{
    private DbContextOptions<TodoContext> DatabaseContextOptions()
    {
        return new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private void PopulateDatabaseContext(TodoContext context)
    {
        context.TodoList.Add(new TodoList { Id = 1, Name = "Task 1" });
        context.TodoList.Add(new TodoList { Id = 2, Name = "Task 2" });
        context.Items.Add(new Item { Id = 1, Name = "Item 1", TodoListId = 1 });
        context.Items.Add(new Item { Id = 2, Name = "Item 2", TodoListId = 1 });
        context.Items.Add(new Item { Id = 3, Name = "Other list item", TodoListId = 2 });
        context.SaveChanges();
    }

    private TodoListsController CreateController(TodoContext context)
    {
        var repository = new TodoListsRepository(context);
        var service = new TodoListsService(repository);

        return new TodoListsController(service, NullLogger<TodoListsController>.Instance);
    }

    [Fact]
    public async Task GetTodoList_WhenCalled_ReturnsTodoListList()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = CreateController(context);

            var result = await controller.GetTodoLists();

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(2, ((result.Result as OkObjectResult).Value as IList<TodoList>).Count);
        }
    }

    [Fact]
    public async Task GetTodoList_WhenCalled_ReturnsTodoListById()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = CreateController(context);

            var result = await controller.GetTodoList(1);

            Assert.IsType<OkObjectResult>(result.Result);
            var todoList = (result.Result as OkObjectResult).Value as TodoListDetail;
            Assert.Equal(1, todoList.Id);
            Assert.Equal(2, todoList.Items.Count);
            Assert.All(todoList.Items, item => Assert.Equal(1, item.TodoListId));
        }
    }

    [Fact]
    public async Task PutTodoList_WhenTodoListDoesntExist_ReturnsBadRequest()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = CreateController(context);

            var result = await controller.PutTodoList(
                3,
                new Dtos.UpdateTodoList { Name = "Task 3" }
            );

            var objectResult = Assert.IsType<ObjectResult>(result);
            var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
            Assert.Equal(404, objectResult.StatusCode);
            Assert.Equal("todo_list_not_found", problemDetails.Extensions["errorCode"]);
        }
    }

    [Fact]
    public async Task PutTodoList_WhenCalled_UpdatesTheTodoList()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = CreateController(context);

            var todoList = await context.TodoList.Where(x => x.Id == 2).FirstAsync();
            var updatedAt = DateTimeOffset.Parse("2026-01-03T00:00:00Z");
            var result = await controller.PutTodoList(
                todoList.Id,
                new Dtos.UpdateTodoList
                {
                    SourceId = "ext-list-2",
                    Name = "Changed Task 2",
                    UpdatedAt = updatedAt,
                }
            );

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("ext-list-2", todoList.SourceId);
            Assert.Equal(updatedAt, todoList.UpdatedAt);
        }
    }

    [Fact]
    public async Task PostTodoList_WhenCalled_CreatesTodoList()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = CreateController(context);

            var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var updatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
            var result = await controller.PostTodoList(
                new Dtos.CreateTodoList
                {
                    SourceId = "ext-list-3",
                    Name = "Task 3",
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                }
            );

            Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.Equal(3, context.TodoList.Count());
            var createdTodoList = (result.Result as CreatedAtActionResult).Value as TodoList;
            Assert.Equal("ext-list-3", createdTodoList.SourceId);
            Assert.Equal(createdAt, createdTodoList.CreatedAt);
            Assert.Equal(updatedAt, createdTodoList.UpdatedAt);
        }
    }

    [Fact]
    public async Task DeleteTodoList_WhenCalled_RemovesTodoList()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = CreateController(context);

            var result = await controller.DeleteTodoList(2);

            Assert.IsType<NoContentResult>(result);
            Assert.Equal(1, context.TodoList.Count());
            Assert.Equal(2, context.TodoList.IgnoreQueryFilters().Count());
        }
    }
}

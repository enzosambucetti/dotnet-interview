using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.Controllers;
using TodoApi.Dtos;
using TodoApi.Models;
using TodoApi.Repositories;
using TodoApi.Services;

namespace TodoApi.Tests.Controllers;

#nullable disable
public class ItemsControllerTests
{
    private DbContextOptions<TodoContext> DatabaseContextOptions()
    {
        return new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private void PopulateDatabaseContext(TodoContext context)
    {
        context.TodoList.Add(new TodoList { Id = 1, Name = "Task list 1" });
        context.TodoList.Add(new TodoList { Id = 2, Name = "Task list 2" });
        context.Items.Add(new Item { Id = 1, Name = "Item 1", TodoListId = 1 });
        context.Items.Add(new Item { Id = 2, Name = "Item 2", TodoListId = 1, IsCompleted = true });
        context.Items.Add(new Item { Id = 3, Name = "Item 3", TodoListId = 2 });
        context.SaveChanges();
    }

    private ItemsController CreateController(TodoContext context)
    {
        var repository = new ItemsRepository(context);
        var service = new ItemsService(repository);

        return new ItemsController(service, NullLogger<ItemsController>.Instance);
    }

    [Fact]
    public async Task GetItems_WhenCalled_ReturnsItemsForTodoList()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);

            var result = await controller.GetItems(1);

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(2, ((result.Result as OkObjectResult).Value as IList<Item>).Count);
        }
    }

    [Fact]
    public async Task GetItems_WhenTodoListDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);

            var result = await controller.GetItems(3);

            AssertNotFoundProblem(result.Result, "todo_list_not_found");
        }
    }

    [Fact]
    public async Task GetItem_WhenItemBelongsToTodoList_ReturnsItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);

            var result = await controller.GetItem(1, 2);

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(2, ((result.Result as OkObjectResult).Value as Item).Id);
        }
    }

    [Fact]
    public async Task GetItem_WhenItemBelongsToAnotherTodoList_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);

            var result = await controller.GetItem(1, 3);

            AssertNotFoundProblem(result.Result, "todo_item_not_found");
        }
    }

    [Fact]
    public async Task PostItem_WhenTodoListExists_CreatesItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);
            var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var updatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z");

            var result = await controller.PostItem(
                1,
                new CreateItem
                {
                    SourceId = "ext-item-4",
                    Name = "New item",
                    IsCompleted = false,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                }
            );
            var item = (result.Result as CreatedAtActionResult).Value as Item;

            Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.Equal(4, context.Items.Count());
            Assert.Equal("ext-item-4", item.SourceId);
            Assert.Equal(createdAt, item.CreatedAt);
            Assert.Equal(updatedAt, item.UpdatedAt);
        }
    }

    [Fact]
    public async Task PostItem_WhenTodoListDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);

            var result = await controller.PostItem(
                3,
                new CreateItem { Name = "New item", IsCompleted = false }
            );

            AssertNotFoundProblem(result.Result, "todo_list_not_found");
        }
    }

    [Fact]
    public async Task PutItem_WhenCalled_UpdatesItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);
            var updatedAt = DateTimeOffset.Parse("2026-01-03T00:00:00Z");

            var result = await controller.PutItem(
                1,
                1,
                new UpdateItem
                {
                    SourceId = "ext-item-1",
                    Name = "Changed item",
                    IsCompleted = true,
                    UpdatedAt = updatedAt,
                }
            );
            var item = await context.Items.FirstAsync(x => x.Id == 1);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("ext-item-1", item.SourceId);
            Assert.Equal("Changed item", item.Name);
            Assert.True(item.IsCompleted);
            Assert.Equal(updatedAt, item.UpdatedAt);
        }
    }

    [Fact]
    public async Task PutItem_WhenItemBelongsToAnotherTodoList_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);

            var result = await controller.PutItem(
                1,
                3,
                new UpdateItem { Name = "Changed item", IsCompleted = true }
            );

            AssertNotFoundProblem(result, "todo_item_not_found");
        }
    }

    [Fact]
    public async Task DeleteItem_WhenCalled_RemovesItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);

            var result = await controller.DeleteItem(1, 2);

            Assert.IsType<NoContentResult>(result);
            Assert.Equal(2, context.Items.Count());
        }
    }

    [Fact]
    public async Task DeleteItem_WhenItemDoesntExistInTodoList_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);
            var controller = CreateController(context);

            var result = await controller.DeleteItem(1, 3);

            AssertNotFoundProblem(result, "todo_item_not_found");
        }
    }

    private static void AssertNotFoundProblem(IActionResult result, string errorCode)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);

        Assert.Equal(404, objectResult.StatusCode);
        Assert.Equal(errorCode, problemDetails.Extensions["errorCode"]);
    }
}
